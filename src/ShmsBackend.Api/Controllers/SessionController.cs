using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.DTOs.Session;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<SessionController> logger)
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

    // POST /api/sessions
    [HttpPost]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> CreateSession([FromBody] CreateViewingSessionDto dto)
    {
        var explorerId = GetCallerId();

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == dto.HouseId);

        if (house == null || house.Flat == null)
            return NotFound(new { success = false, message = "House not found." });

        var agentFlat = await _context.AgentFlats
            .Include(af => af.Agent)
            .FirstOrDefaultAsync(af => af.FlatId == house.FlatId);

        if (agentFlat == null)
            return BadRequest(new
            {
                success = false,
                errorType = "NoAgentAssigned",
                message = "No agent is currently assigned to this flat. Please raise a complaint or contact management before proceeding."
            });

        var session = new ListingViewingSession
        {
            Id = Guid.NewGuid(),
            HouseId = dto.HouseId,
            ExplorerId = explorerId,
            AgentId = agentFlat.AgentId,
            ScheduledAt = dto.ScheduledAt,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ListingViewingSessions.Add(session);
        await _context.SaveChangesAsync();

        var agent = agentFlat.Agent;
        var houseNumber = house.HouseNumber;

        try { await _emailService.SendSessionRequestAgentEmailAsync(agent.Email, agent.FirstName, houseNumber, dto.ScheduledAt); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send session request email to agent {AgentId}", agent.Id); }

        try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"A viewing session has been requested for house {houseNumber} and needs your acceptance.", "property"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of viewing session request"); }

        return Ok(new { success = true, data = new { session.Id } });
    }

    // PATCH /api/sessions/{id}/accept
    [HttpPatch("{id:guid}/accept")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> AcceptSession(Guid id)
    {
        var callerId = GetCallerId();

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.AgentId != callerId)
            return BadRequest(new { success = false, message = "You are not assigned to this session." });

        if (session.Status != "Pending")
            return BadRequest(new { success = false, message = "Only pending sessions can be accepted." });

        session.Status = "Accepted";
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FindAsync(session.HouseId);
        var agent = await _context.Agents.FindAsync(callerId);
        var explorer = await _context.Explorers.FindAsync(session.ExplorerId);

        var houseNumber = house?.HouseNumber ?? "";
        var agentName = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : "";
        var agentPhone = agent?.PhoneNumber ?? "";

        if (explorer != null)
        {
            try { await _emailService.SendSessionConfirmedExplorerEmailAsync(explorer.Email, explorer.FirstName, houseNumber, agentName, agentPhone, session.ScheduledAt); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send session confirmed email to explorer {ExplorerId}", explorer.Id); }

            try { await _notificationService.SendToUserAsync(explorer.Id.ToString(), $"Your viewing session for house {houseNumber} has been confirmed by the agent.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify explorer of session confirmation"); }
        }

        return Ok(new { success = true, message = "Session accepted." });
    }

    // PATCH /api/sessions/{id}/decline
    [HttpPatch("{id:guid}/decline")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> DeclineSession(Guid id, [FromBody] DeclineViewingSessionDto dto)
    {
        var callerId = GetCallerId();

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.AgentId != callerId)
            return BadRequest(new { success = false, message = "You are not assigned to this session." });

        if (session.Status != "Pending")
            return BadRequest(new { success = false, message = "Only pending sessions can be declined." });

        session.Status = "Declined";
        session.DeclineReason = dto.Reason;
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FindAsync(session.HouseId);
        var agent = await _context.Agents.FindAsync(callerId);

        var houseNumber = house?.HouseNumber ?? "";
        var agentName = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : "";

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Agent {agentName} declined a viewing session for house {houseNumber}, reassignment needed.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of session decline"); }

        try
        {
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
            foreach (var mgr in managementUsers)
            {
                try { await _emailService.SendSessionDeclinedManagementEmailAsync(mgr.Email, mgr.FirstName, houseNumber, agentName); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send session declined email to {Email}", mgr.Email); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for session declined email"); }

        return Ok(new { success = true, message = "Session declined." });
    }
}
