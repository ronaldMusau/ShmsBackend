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

        // Capacity-crossing check — fire exactly at the 5 mark
        var targetDate = dto.ScheduledAt.Date;
        var nextDate = targetDate.AddDays(1);
        var liveCount = await _context.ListingViewingSessions.CountAsync(s =>
            s.AgentId == agentFlat.AgentId &&
            s.ScheduledAt >= targetDate &&
            s.ScheduledAt < nextDate &&
            (s.Status == "Pending" || s.Status == "Accepted" || s.Status == "AwaitingFeedback"));

        if (liveCount == 5)
        {
            var agentName = $"{agentFlat.Agent.FirstName} {agentFlat.Agent.LastName}".Trim();
            var scheduledDate = targetDate.ToString("yyyy-MM-dd");

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Agent {agentName} now has 5+ sessions on {scheduledDate}, consider reassigning.",
                    "property");
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of agent capacity crossing"); }

            try
            {
                var superAdmins = await _context.SuperAdmins.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
                var adminUsers = await _context.AdminUsers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
                var managers = await _context.Managers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
                var secretaries = await _context.Secretaries.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
                var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
                foreach (var mgr in managementUsers)
                {
                    try { await _emailService.SendSessionCapacityAlertEmailAsync(mgr.Email, mgr.FirstName, agentName, scheduledDate, mgr.Id.ToString(), false); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send capacity alert email to {Email}", mgr.Email); }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for capacity alert email"); }
        }

        var agent = agentFlat.Agent;
        var houseNumber = house.HouseNumber;

        try { await _emailService.SendSessionRequestAgentEmailAsync(agent.Email, agent.FirstName, houseNumber, dto.ScheduledAt, agent.Id.ToString(), true); }
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
            try { await _emailService.SendSessionConfirmedExplorerEmailAsync(explorer.Email, explorer.FirstName, houseNumber, agentName, agentPhone, session.ScheduledAt, explorer.Id.ToString(), true); }
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
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
            foreach (var mgr in managementUsers)
            {
                try { await _emailService.SendSessionDeclinedManagementEmailAsync(mgr.Email, mgr.FirstName, houseNumber, agentName, mgr.Id.ToString(), false); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send session declined email to {Email}", mgr.Email); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for session declined email"); }

        return Ok(new { success = true, message = "Session declined." });
    }

    // PATCH /api/sessions/{id}/reassign
    [HttpPatch("{id:guid}/reassign")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> ReassignSession(Guid id, [FromBody] ReassignViewingSessionDto dto)
    {
        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.Status == "Closed" || session.Status == "Forfeited")
            return BadRequest(new { success = false, message = "This session cannot be reassigned." });

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == session.HouseId);

        if (house?.Flat == null)
            return BadRequest(new { success = false, message = "House flat information not found." });

        var targetWard = house.Flat.Ward;
        var wardAgentIds = await _context.AgentFlats
            .Where(af => af.Flat.Ward == targetWard)
            .Select(af => af.AgentId)
            .Distinct()
            .ToListAsync();

        if (!wardAgentIds.Contains(dto.NewAgentId))
            return BadRequest(new { success = false, message = "Selected agent does not cover this ward." });

        session.ReassignedFromAgentId = session.AgentId;
        session.AgentId = dto.NewAgentId;
        session.ReassignedAt = DateTime.UtcNow;
        session.Status = "Pending";
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var newAgent = await _context.Agents.FindAsync(dto.NewAgentId);
        var explorer = await _context.Explorers.FindAsync(session.ExplorerId);
        var houseNumber = house.HouseNumber;

        if (newAgent != null)
        {
            try { await _emailService.SendSessionRequestAgentEmailAsync(newAgent.Email, newAgent.FirstName, houseNumber, session.ScheduledAt, newAgent.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send session request email to reassigned agent {AgentId}", newAgent.Id); }

            try { await _notificationService.SendToUserAsync(newAgent.Id.ToString(), $"A viewing session for house {houseNumber} has been assigned to you and needs your acceptance.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify reassigned agent of session"); }
        }

        if (explorer != null)
        {
            var agentName = newAgent != null ? $"{newAgent.FirstName} {newAgent.LastName}".Trim() : "";
            var agentPhone = newAgent?.PhoneNumber ?? "";

            try { await _emailService.SendSessionReassignedExplorerEmailAsync(explorer.Email, explorer.FirstName, houseNumber, agentName, agentPhone, session.ScheduledAt, explorer.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send session reassigned email to explorer {ExplorerId}", explorer.Id); }

            try { await _notificationService.SendToUserAsync(explorer.Id.ToString(), $"Your viewing session for house {houseNumber} has been reassigned to a new agent and is pending their acceptance.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify explorer of session reassignment"); }
        }

        return Ok(new { success = true, message = "Session reassigned." });
    }

    // PATCH /api/sessions/{id}/reschedule
    [HttpPatch("{id:guid}/reschedule")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> RescheduleSession(Guid id, [FromBody] RescheduleViewingSessionDto dto)
    {
        var callerId = GetCallerId();

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.ExplorerId != callerId)
            return Forbid();

        var allowedStatuses = new[] { "Pending", "Accepted", "AwaitingFeedback" };
        if (!allowedStatuses.Contains(session.Status))
            return BadRequest(new { success = false, message = "This session cannot be rescheduled." });

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { success = false, message = "A reason is required to reschedule." });

        session.PreviousScheduledAt = session.ScheduledAt;
        session.ScheduledAt = dto.NewScheduledAt;
        session.RescheduleReason = dto.Reason;
        session.RescheduleCount += 1;
        session.Status = "Pending";
        session.FeedbackPromptSentAt = null;
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var agent = await _context.Agents.FindAsync(session.AgentId);
        var house = await _context.Houses.FindAsync(session.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        if (agent != null)
        {
            try { await _emailService.SendSessionRequestAgentEmailAsync(agent.Email, agent.FirstName, houseNumber, dto.NewScheduledAt, agent.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send reschedule email to agent {AgentId}", agent.Id); }

            try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"The viewing session for house {houseNumber} has been rescheduled and needs your acceptance for the new time.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of session reschedule"); }
        }

        return Ok(new { success = true, message = "Session rescheduled." });
    }

    // PATCH /api/sessions/{id}/close
    [HttpPatch("{id:guid}/close")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> CloseSession(Guid id, [FromBody] CloseViewingSessionDto dto)
    {
        var callerId = GetCallerId();

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.ExplorerId != callerId)
            return Forbid();

        if (session.Status != "AwaitingFeedback")
            return BadRequest(new { success = false, message = "Only sessions awaiting feedback can be closed." });

        if (dto.Rating.HasValue && (dto.Rating.Value < 1 || dto.Rating.Value > 5))
            return BadRequest(new { success = false, message = "Rating must be between 1 and 5." });

        session.Status = "Closed";
        session.ClosedAt = DateTime.UtcNow;
        session.ClosingComment = dto.Comment;
        session.AgentRating = dto.Rating;
        session.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Session closed." });
    }

    // GET /api/sessions/{id}/messages
    [HttpGet("{id:guid}/messages")]
    [Authorize(Roles = "Explorer,Agent")]
    public async Task<IActionResult> GetSessionMessages(Guid id, [FromQuery] Guid? agentId)
    {
        var callerId = GetCallerId();
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        Guid resolvedAgentId;

        if (callerRole == "Agent")
        {
            if (session.AgentId != callerId)
                return Forbid();
            resolvedAgentId = callerId;
        }
        else
        {
            if (session.ExplorerId != callerId)
                return Forbid();

            resolvedAgentId = agentId ?? session.AgentId;

            if (resolvedAgentId != session.AgentId)
            {
                var hasMessages = await _context.SessionMessages
                    .AnyAsync(m => m.SessionId == id && m.AgentId == resolvedAgentId);
                if (!hasMessages)
                    return NotFound(new { success = false, message = "No messages found for this agent thread." });
            }
        }

        var messages = await _context.SessionMessages
            .Where(m => m.SessionId == id && m.AgentId == resolvedAgentId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (callerRole == "Agent")
        {
            var unread = messages.Where(m => m.SenderRole == "Explorer" && !m.IsReadByAgent).ToList();
            foreach (var msg in unread)
                msg.IsReadByAgent = true;
            if (unread.Count > 0)
                await _context.SaveChangesAsync();
        }
        else
        {
            var unread = messages.Where(m => m.SenderRole == "Agent" && !m.IsReadByExplorer).ToList();
            foreach (var msg in unread)
                msg.IsReadByExplorer = true;
            if (unread.Count > 0)
                await _context.SaveChangesAsync();
        }

        var canReply = resolvedAgentId == session.AgentId &&
                       (session.Status == "Accepted" || session.Status == "AwaitingFeedback");

        return Ok(new
        {
            success = true,
            canReply,
            messages = messages.Select(m => new
            {
                m.Id,
                m.SenderRole,
                m.SenderUserId,
                m.Message,
                m.CreatedAt,
                m.IsReadByAgent,
                m.IsReadByExplorer
            })
        });
    }

    // GET /api/sessions/{id}/agent-threads
    [HttpGet("{id:guid}/agent-threads")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> GetAgentThreads(Guid id)
    {
        var callerId = GetCallerId();

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        if (session.ExplorerId != callerId)
            return Forbid();

        var threads = await _context.SessionMessages
            .Where(m => m.SessionId == id)
            .GroupBy(m => m.AgentId)
            .Select(g => new { AgentId = g.Key, LastMessageAt = g.Max(m => m.CreatedAt) })
            .ToListAsync();

        var agentIds = threads.Select(t => t.AgentId).ToList();
        var agents = await _context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.FirstName, a.LastName })
            .ToListAsync();

        var agentDict = agents.ToDictionary(a => a.Id, a => $"{a.FirstName} {a.LastName}".Trim());

        var data = threads.Select(t => new
        {
            agentId = t.AgentId,
            agentName = agentDict.TryGetValue(t.AgentId, out var name) ? name : "",
            lastMessageAt = t.LastMessageAt,
            isActive = t.AgentId == session.AgentId
        }).ToList();

        return Ok(new { success = true, data });
    }

    // POST /api/sessions/{id}/messages
    [HttpPost("{id:guid}/messages")]
    [Authorize(Roles = "Explorer,Agent")]
    public async Task<IActionResult> SendSessionMessage(Guid id, [FromBody] SendSessionMessageDto dto)
    {
        var callerId = GetCallerId();
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        Guid resolvedAgentId;
        string senderRole;

        if (callerRole == "Agent")
        {
            resolvedAgentId = callerId;
            senderRole = "Agent";
        }
        else
        {
            if (session.ExplorerId != callerId)
                return Forbid();
            resolvedAgentId = session.AgentId;
            senderRole = "Explorer";
        }

        if (resolvedAgentId != session.AgentId)
            return BadRequest(new { success = false, message = "This conversation is closed." });

        if (session.Status != "Accepted" && session.Status != "AwaitingFeedback")
            return BadRequest(new { success = false, message = "This conversation is closed." });

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { success = false, message = "Message cannot be empty." });

        var message = new SessionMessage
        {
            SessionId = id,
            AgentId = resolvedAgentId,
            SenderRole = senderRole,
            SenderUserId = callerId,
            Message = dto.Message,
            IsReadByAgent = senderRole == "Agent",
            IsReadByExplorer = senderRole == "Explorer",
            CreatedAt = DateTime.UtcNow
        };

        _context.SessionMessages.Add(message);
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FindAsync(session.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        if (senderRole == "Agent")
        {
            try { await _notificationService.SendToUserAsync(session.ExplorerId.ToString(), $"New message on your viewing session for house {houseNumber}.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify explorer of new agent message"); }
        }
        else
        {
            try { await _notificationService.SendToUserAsync(session.AgentId.ToString(), $"New message on the viewing session for house {houseNumber}.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of new explorer message"); }
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                message.Id,
                message.SenderRole,
                message.SenderUserId,
                message.Message,
                message.CreatedAt,
                message.IsReadByAgent,
                message.IsReadByExplorer
            }
        });
    }

    // GET /api/sessions/my-sessions
    [HttpGet("my-sessions")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 6)
    {
        var explorerId = GetCallerId();

        var query = _context.ListingViewingSessions
            .Where(s => s.ExplorerId == explorerId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync();

        var sessions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var agentIds = sessions.Select(s => s.AgentId).Distinct().ToList();

        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToListAsync();

        var agents = await _context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ToListAsync();

        var houseDict = houses.ToDictionary(h => h.Id);
        var agentDict = agents.ToDictionary(a => a.Id);

        var data = sessions.Select(s =>
        {
            houseDict.TryGetValue(s.HouseId, out var house);
            agentDict.TryGetValue(s.AgentId, out var agent);
            return (object)new
            {
                id = s.Id,
                houseNumber = house?.HouseNumber,
                flatName = house?.Flat?.FlatName,
                agentName = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : (string?)null,
                agentPhone = agent?.PhoneNumber,
                scheduledAt = s.ScheduledAt,
                status = s.Status,
                closingComment = s.ClosingComment,
                agentRating = s.AgentRating,
                declineReason = s.DeclineReason,
                rescheduleCount = s.RescheduleCount
            };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    // GET /api/sessions/agent-sessions
    [HttpGet("agent-sessions")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetAgentSessions(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 6)
    {
        var agentId = GetCallerId();

        var query = _context.ListingViewingSessions
            .Where(s => s.AgentId == agentId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync();

        var sessions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var explorerIds = sessions.Select(s => s.ExplorerId).Distinct().ToList();

        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToListAsync();

        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToListAsync();

        var houseDict = houses.ToDictionary(h => h.Id);
        var explorerDict = explorers.ToDictionary(e => e.Id);

        var data = sessions.Select(s =>
        {
            houseDict.TryGetValue(s.HouseId, out var house);
            explorerDict.TryGetValue(s.ExplorerId, out var explorer);
            return (object)new
            {
                id = s.Id,
                houseNumber = house?.HouseNumber,
                flatName = house?.Flat?.FlatName,
                explorerName = explorer != null ? $"{explorer.FirstName} {explorer.LastName}".Trim() : (string?)null,
                explorerPhone = explorer?.PhoneNumber,
                scheduledAt = s.ScheduledAt,
                status = s.Status,
                declineReason = s.DeclineReason,
                rescheduleReason = s.RescheduleReason,
                rescheduleCount = s.RescheduleCount
            };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    // GET /api/sessions/all
    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetAllSessions(
        [FromQuery] string? status,
        [FromQuery] Guid? agentId,
        [FromQuery] bool? needsReassignment,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.ListingViewingSessions.AsQueryable();

        if (needsReassignment == true)
            query = query.Where(s => s.Status == "Declined");
        else if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);

        if (agentId.HasValue)
            query = query.Where(s => s.AgentId == agentId.Value);

        if (fromDate.HasValue)
            query = query.Where(s => s.ScheduledAt >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(s => s.ScheduledAt < toDate.Value.Date.AddDays(1));

        var total = await query.CountAsync();

        var sessions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var sessionAgentIds = sessions.Select(s => s.AgentId).Distinct().ToList();
        var explorerIds = sessions.Select(s => s.ExplorerId).Distinct().ToList();

        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToListAsync();

        var agents = await _context.Agents
            .Where(a => sessionAgentIds.Contains(a.Id))
            .ToListAsync();

        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToListAsync();

        var houseDict = houses.ToDictionary(h => h.Id);
        var agentDict = agents.ToDictionary(a => a.Id);
        var explorerDict = explorers.ToDictionary(e => e.Id);

        // Bulk load live session counts per (AgentId, Date) for the page's agents/dates
        List<(Guid AgentId, DateTime Date)> liveSessionData = new();
        if (sessions.Any())
        {
            var minDate = sessions.Min(s => s.ScheduledAt.Date);
            var maxDate = sessions.Max(s => s.ScheduledAt.Date).AddDays(1);
            var liveRaw = await _context.ListingViewingSessions
                .Where(s => sessionAgentIds.Contains(s.AgentId) &&
                            s.ScheduledAt >= minDate &&
                            s.ScheduledAt < maxDate &&
                            (s.Status == "Pending" || s.Status == "Accepted" || s.Status == "AwaitingFeedback"))
                .Select(s => new { s.AgentId, s.ScheduledAt })
                .ToListAsync();
            liveSessionData = liveRaw.Select(x => (x.AgentId, x.ScheduledAt.Date)).ToList();
        }

        var loadCountDict = liveSessionData
            .GroupBy(x => (x.AgentId, x.Date))
            .ToDictionary(g => g.Key, g => g.Count());

        var data = sessions.Select(s =>
        {
            houseDict.TryGetValue(s.HouseId, out var house);
            agentDict.TryGetValue(s.AgentId, out var agent);
            explorerDict.TryGetValue(s.ExplorerId, out var explorer);
            var agentDayCount = loadCountDict.TryGetValue((s.AgentId, s.ScheduledAt.Date), out var cnt) ? cnt : 0;
            var loadLevel = agentDayCount > 4 ? "Red" : agentDayCount == 4 ? "Amber" : "Green";
            return (object)new
            {
                id = s.Id,
                houseNumber = house?.HouseNumber,
                flatName = house?.Flat?.FlatName,
                agentId = s.AgentId,
                agentName = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : (string?)null,
                explorerName = explorer != null ? $"{explorer.FirstName} {explorer.LastName}".Trim() : (string?)null,
                explorerPhone = explorer?.PhoneNumber,
                scheduledAt = s.ScheduledAt,
                status = s.Status,
                declineReason = s.DeclineReason,
                loadLevel,
                agentDayCount
            };
        }).ToList();

        var totals = new
        {
            totalPending = await _context.ListingViewingSessions.CountAsync(s => s.Status == "Pending"),
            totalAccepted = await _context.ListingViewingSessions.CountAsync(s => s.Status == "Accepted"),
            totalAwaitingFeedback = await _context.ListingViewingSessions.CountAsync(s => s.Status == "AwaitingFeedback"),
            totalDeclinedNeedsReassignment = await _context.ListingViewingSessions.CountAsync(s => s.Status == "Declined")
        };

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals
        });
    }

    // GET /api/sessions/agent-calendar/{agentId}
    [HttpGet("agent-calendar/{agentId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetAgentCalendar(
        Guid agentId,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        var fromStart = fromDate.Date;
        var toEnd = toDate.Date.AddDays(1);

        var sessions = await _context.ListingViewingSessions
            .Where(s => s.AgentId == agentId &&
                        s.ScheduledAt >= fromStart &&
                        s.ScheduledAt < toEnd &&
                        (s.Status == "Pending" || s.Status == "Accepted" || s.Status == "AwaitingFeedback"))
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var explorerIds = sessions.Select(s => s.ExplorerId).Distinct().ToList();

        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim());

        var data = sessions
            .GroupBy(s => s.ScheduledAt.Date)
            .Select(g =>
            {
                var count = g.Count();
                return (object)new
                {
                    date = g.Key,
                    count,
                    loadLevel = count > 4 ? "Red" : count == 4 ? "Amber" : "Green",
                    sessions = g.OrderBy(s => s.ScheduledAt).Select(s => new
                    {
                        houseNumber = houses.GetValueOrDefault(s.HouseId),
                        explorerName = explorers.GetValueOrDefault(s.ExplorerId),
                        scheduledAt = s.ScheduledAt,
                        status = s.Status
                    }).ToList()
                };
            })
            .OrderBy(d => ((dynamic)d).date)
            .ToList();

        return Ok(new { success = true, data });
    }

    // GET /api/sessions/{id}/candidate-agents
    [HttpGet("{id:guid}/candidate-agents")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetCandidateAgents(Guid id)
    {
        var session = await _context.ListingViewingSessions.FindAsync(id);
        if (session == null)
            return NotFound(new { success = false, message = "Session not found." });

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == session.HouseId);

        if (house?.Flat == null)
            return BadRequest(new { success = false, message = "House flat information not found." });

        var targetWard = house.Flat.Ward;
        var candidateAgentIds = await _context.AgentFlats
            .Where(af => af.Flat.Ward == targetWard)
            .Select(af => af.AgentId)
            .Distinct()
            .ToListAsync();

        var candidates = await _context.Agents
            .Where(a => candidateAgentIds.Contains(a.Id))
            .ToListAsync();

        var scheduledDate = session.ScheduledAt.Date;
        var nextDate = scheduledDate.AddDays(1);

        var dayLoadCounts = await _context.ListingViewingSessions
            .Where(s => candidateAgentIds.Contains(s.AgentId) &&
                        s.ScheduledAt >= scheduledDate &&
                        s.ScheduledAt < nextDate &&
                        (s.Status == "Pending" || s.Status == "Accepted" || s.Status == "AwaitingFeedback"))
            .GroupBy(s => s.AgentId)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToListAsync();

        var loadDict = dayLoadCounts.ToDictionary(x => x.AgentId, x => x.Count);

        var data = candidates.Select(a =>
        {
            var dayLoad = loadDict.TryGetValue(a.Id, out var cnt) ? cnt : 0;
            return (object)new
            {
                agentId = a.Id,
                agentName = $"{a.FirstName} {a.LastName}".Trim(),
                currentDayLoad = dayLoad
            };
        }).ToList();

        return Ok(new { success = true, data });
    }
}
