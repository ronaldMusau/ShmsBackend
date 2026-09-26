using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/explorer-interest")]
[Authorize]
public class ExplorerInterestController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ExplorerInterestController> _logger;

    public ExplorerInterestController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<ExplorerInterestController> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    private Guid GetCallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // Same availability computation GetUpcomingListings uses: the most recent Approved, non-deleted
    // VacateRequest for this house, by CreatedAt.
    private async Task<(string AvailabilityText, int? Month, int? Year)> ResolveAvailabilityAsync(House house)
    {
        if (house.OccupancyStatus == OccupancyStatus.Vacant)
            return ("now", null, null);

        var vacate = await _context.VacateRequests
            .Where(v => v.HouseId == house.Id && v.Status == "Approved" && !v.IsDeleted)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new { v.VacateMonth, v.VacateYear })
            .FirstOrDefaultAsync();

        if (vacate == null)
            return ("soon — no confirmed date yet", null, null);

        var monthName = new DateTime(vacate.VacateYear, vacate.VacateMonth, 1).ToString("MMMM yyyy");
        return ($"from {monthName}", vacate.VacateMonth, vacate.VacateYear);
    }

    // GET /api/explorer-interest/preview/{houseId} — availability info for the confirmation modal
    [HttpGet("preview/{houseId:guid}")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> GetPreview(Guid houseId)
    {
        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null)
            return NotFound(new { success = false, message = "House not found." });

        var (availabilityText, month, year) = await ResolveAvailabilityAsync(house);

        return Ok(new
        {
            success = true,
            data = new
            {
                houseNumber = house.HouseNumber,
                flatName = house.Flat?.FlatName,
                isVacantNow = house.OccupancyStatus == OccupancyStatus.Vacant,
                availabilityText,
                availableFromMonth = month,
                availableFromYear = year
            }
        });
    }

    // POST /api/explorer-interest — confirm interest
    [HttpPost]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> Create([FromBody] CreateExplorerInterestDto dto)
    {
        var explorerId = GetCallerId();
        var explorer = await _context.Explorers.FirstOrDefaultAsync(e => e.Id == explorerId);
        if (explorer == null)
            return Unauthorized();

        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == dto.HouseId);
        if (house == null)
            return NotFound(new { success = false, message = "House not found." });

        var (availabilityText, _, _) = await ResolveAvailabilityAsync(house);

        var interest = new ExplorerInterest
        {
            ExplorerId = explorerId,
            HouseId = dto.HouseId,
            SessionId = dto.SessionId,
            Status = "Pending"
        };
        _context.ExplorerInterests.Add(interest);
        await _context.SaveChangesAsync();

        var houseNumber = house.HouseNumber;
        var flatName = house.Flat?.FlatName ?? "";
        var message = $"An explorer has expressed interest in house {houseNumber} at {flatName}, available {availabilityText}.";

        // Resolve the house's current agent via AgentFlat (same pattern used elsewhere for
        // flat-level agent lookups — ListingViewingSession.AgentId is per-session, not authoritative
        // for "who currently handles this flat").
        var agentFlat = await _context.AgentFlats
            .Include(af => af.Agent)
            .FirstOrDefaultAsync(af => af.FlatId == house.FlatId);

        if (agentFlat != null)
        {
            try
            {
                await _notificationService.SendForcedToUserAsync(
                    agentFlat.AgentId.ToString(), message, "property", "ExplorerInterest", interest.Id.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send forced notification to agent {AgentId} for explorer interest {InterestId}", agentFlat.AgentId, interest.Id);
            }

            try
            {
                // userId: null bypasses ShouldSendEmailAsync's preference gate entirely — this
                // codebase has no separate "forced email" path, so a forced send is a normal send
                // with no user identity threaded through (see ShouldSendEmailAsync's own doc comment).
                await _emailService.SendExplorerInterestAgentEmailAsync(
                    agentFlat.Agent.Email, agentFlat.Agent.FirstName, houseNumber, flatName, availabilityText, null, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send forced email to agent {AgentId} for explorer interest {InterestId}", agentFlat.AgentId, interest.Id);
            }
        }

        // Forced notification to every Management user — same role-list convention as
        // ComplaintReminderSchedulerService's management alerts, but called via the forced path.
        var superAdmins = await _context.SuperAdmins.Select(u => u.Id).ToListAsync();
        var adminUsers = await _context.AdminUsers.Select(u => u.Id).ToListAsync();
        var managers = await _context.Managers.Select(u => u.Id).ToListAsync();
        var secretaries = await _context.Secretaries.Select(u => u.Id).ToListAsync();
        var managementIds = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();

        foreach (var mgrId in managementIds)
        {
            try
            {
                await _notificationService.SendForcedToUserAsync(
                    mgrId.ToString(), message, "property", "ExplorerInterest", interest.Id.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send forced notification to management user {UserId} for explorer interest {InterestId}", mgrId, interest.Id);
            }
        }

        // Forced push confirmation to the explorer themselves.
        try
        {
            await _notificationService.SendForcedToUserAsync(
                explorerId.ToString(),
                $"Your interest in house {houseNumber} at {flatName} ({availabilityText}) has been recorded. The agent and management have been notified.",
                "property", "ExplorerInterest", interest.Id.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send forced confirmation to explorer {ExplorerId} for interest {InterestId}", explorerId, interest.Id);
        }

        return Ok(new { success = true, data = new { interest.Id, interest.Status } });
    }
}

public class CreateExplorerInterestDto
{
    public Guid HouseId { get; set; }
    public Guid? SessionId { get; set; }
}
