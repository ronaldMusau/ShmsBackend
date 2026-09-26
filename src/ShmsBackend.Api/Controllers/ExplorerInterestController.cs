using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Portal;
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
    private readonly ITenantService _tenantService;
    private readonly ILogger<ExplorerInterestController> _logger;

    public ExplorerInterestController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ITenantService tenantService,
        ILogger<ExplorerInterestController> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _tenantService = tenantService;
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

        // If this interest traces back to a specific viewing session, and that session's original
        // agent differs from the flat's current agent, give the original agent a separate FYI —
        // they showed the house, but the CURRENT agent is who will actually handle onboarding.
        if (interest.SessionId.HasValue)
        {
            var originalSession = await _context.ListingViewingSessions
                .FirstOrDefaultAsync(s => s.Id == interest.SessionId.Value);

            if (originalSession != null && (agentFlat == null || originalSession.AgentId != agentFlat.AgentId))
            {
                var originalAgent = await _context.Agents.FirstOrDefaultAsync(a => a.Id == originalSession.AgentId);
                if (originalAgent != null)
                {
                    var currentAgentName = agentFlat != null
                        ? $"{agentFlat.Agent.FirstName} {agentFlat.Agent.LastName}".Trim()
                        : "another agent";
                    var fyiMessage = $"An explorer you showed house {houseNumber} to has expressed interest — {currentAgentName} is now handling this flat and will proceed with onboarding.";

                    try
                    {
                        await _notificationService.SendForcedToUserAsync(
                            originalAgent.Id.ToString(), fyiMessage, "property", "ExplorerInterest", interest.Id.ToString());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send FYI notification to original session agent {AgentId} for explorer interest {InterestId}", originalAgent.Id, interest.Id);
                    }

                    try
                    {
                        await _emailService.SendExplorerInterestOriginalAgentFyiEmailAsync(
                            originalAgent.Email, originalAgent.FirstName, houseNumber, flatName, currentAgentName, null, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send FYI email to original session agent {AgentId} for explorer interest {InterestId}", originalAgent.Id, interest.Id);
                    }
                }
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

    // POST /api/explorer-interest/{id}/convert — Management pre-fills and creates a Tenant from
    // this interest's Explorer/House. Not a payment step — payment-related fields stay at defaults.
    [HttpPost("{id:guid}/convert")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> ConvertToTenant(Guid id)
    {
        var interest = await _context.ExplorerInterests.FirstOrDefaultAsync(ei => ei.Id == id);
        if (interest == null)
            return NotFound(new { success = false, message = "Explorer interest not found." });

        if (interest.Status != "Pending")
            return BadRequest(new { success = false, message = "This interest is no longer pending and cannot be converted." });

        var explorer = await _context.Explorers.FirstOrDefaultAsync(e => e.Id == interest.ExplorerId);
        if (explorer == null)
            return NotFound(new { success = false, message = "Explorer not found." });

        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == interest.HouseId);
        if (house == null)
            return NotFound(new { success = false, message = "House not found." });

        // A one-click conversion has no caller-supplied password — generate a temporary one the same
        // way EmailVerificationToken generation elsewhere in TenantService does (Guid-derived, random).
        // It flows into Tenant.TemporaryInitialPassword and reaches the tenant via the existing
        // account-ready email once their initial payment completes — same as any other tenant.
        var temporaryPassword = Guid.NewGuid().ToString("N").Substring(0, 12);

        var dto = new CreateTenantDto
        {
            Email = explorer.Email,
            Password = temporaryPassword,
            FirstName = explorer.FirstName,
            LastName = explorer.LastName,
            PhoneNumber = explorer.PhoneNumber,
            HouseId = interest.HouseId,
            SourceExplorerInterestId = interest.Id
        };

        Tenant tenant;
        try
        {
            tenant = await _tenantService.CreateAsync(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                tenantId = tenant.Id,
                houseId = house.Id,
                fullName = $"{tenant.FirstName} {tenant.LastName}".Trim()
            }
        });
    }
}

public class CreateExplorerInterestDto
{
    public Guid HouseId { get; set; }
    public Guid? SessionId { get; set; }
}
