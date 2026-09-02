using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentController> _logger;
    private readonly ShmsDbContext _context;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IEmailService _emailService;
    private readonly IAgreementService _agreementService;

    public AgentController(
        IAgentService agentService,
        ILogger<AgentController> logger,
        ShmsDbContext context,
        IFrontendUrlService frontendUrlService,
        IEmailService emailService,
        IAgreementService agreementService)
    {
        _agentService = agentService;
        _logger = logger;
        _context = context;
        _frontendUrlService = frontendUrlService;
        _emailService = emailService;
        _agreementService = agreementService;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateAgentDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var agent = await _agentService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = agent.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    agent.Id,
                    agent.Email,
                    agent.FirstName,
                    agent.LastName,
                    agent.PhoneNumber,
                    agent.AgencyName,
                    agent.LicenseNumber,
                    agent.County,
                    agent.Constituency,
                    agent.Ward,
                    agent.IsActive,
                    agent.PortalUserType
                }, "Agent created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the agent"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var agent = await _agentService.GetByIdAsync(id);
            if (agent == null)
                return NotFound(ApiResponse<object>.FailureResponse("Agent not found"));

            var ratedSessions = _context.ListingViewingSessions
                .Where(s => s.AgentId == id && s.Status == "Closed" && s.AgentRating != null);
            var ratingCount = await ratedSessions.CountAsync();
            var averageRating = ratingCount > 0
                ? await ratedSessions.AverageAsync(s => (double)s.AgentRating!.Value)
                : (double?)null;

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                agent.Id,
                agent.Email,
                agent.FirstName,
                agent.LastName,
                agent.PhoneNumber,
                agent.AgencyName,
                agent.LicenseNumber,
                agent.County,
                agent.Constituency,
                agent.Ward,
                agent.IsActive,
                agent.IsEmailVerified,
                agent.PortalUserType,
                agent.CreatedAt,
                agent.UpdatedAt,
                averageRating,
                ratingCount
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the agent"));
        }
    }

    // GET /api/agent/{id}/detail
    // Composite admin view: profile + rating + assigned flats + escalated complaints + viewing sessions + agreement/ID status.
    [HttpGet("{id:guid}/detail")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        try
        {
            var agent = await _agentService.GetByIdAsync(id);
            if (agent == null)
                return NotFound(ApiResponse<object>.FailureResponse("Agent not found"));

            // ── Rating average + count (same query as GetById) ──
            var ratedSessions = _context.ListingViewingSessions
                .Where(s => s.AgentId == id && s.Status == "Closed" && s.AgentRating != null);
            var ratingCount = await ratedSessions.CountAsync();
            var averageRating = ratingCount > 0
                ? await ratedSessions.AverageAsync(s => (double)s.AgentRating!.Value)
                : (double?)null;

            // ── Assigned flats (clickable) — mirrors GetFlats ──
            var flats = await _context.AgentFlats
                .Include(af => af.Flat)
                .Where(af => af.AgentId == id)
                .Select(af => new
                {
                    af.Flat.Id,
                    af.Flat.FlatName,
                    af.Flat.County,
                    af.Flat.Constituency,
                    af.Flat.Ward,
                    af.AssignedAt
                })
                .ToListAsync();

            // ── Complaints escalated to this agent (clickable) ──
            var complaints = await _context.Complaints
                .Include(c => c.ComplaintType)
                .Where(c => c.EscalatedToAgentId == id)
                .OrderByDescending(c => c.EscalatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.TicketNumber,
                    ComplaintTypeName = c.ComplaintType != null ? c.ComplaintType.Name : null,
                    c.Status,
                    c.CreatedAt,
                    c.EscalatedAt
                })
                .ToListAsync();

            // ── Viewing sessions (clickable) — mirrors SessionController.GetAgentSessions projection ──
            var totalSessions = await _context.ListingViewingSessions.CountAsync(s => s.AgentId == id);
            var sessionsRaw = await _context.ListingViewingSessions
                .Where(s => s.AgentId == id)
                .OrderByDescending(s => s.CreatedAt)
                .Take(10)
                .ToListAsync();
            var sHouseIds = sessionsRaw.Select(s => s.HouseId).Distinct().ToList();
            var sExplorerIds = sessionsRaw.Select(s => s.ExplorerId).Distinct().ToList();
            var sHouses = (await _context.Houses
                    .Include(hh => hh.Flat)
                    .Where(hh => sHouseIds.Contains(hh.Id))
                    .ToListAsync())
                .ToDictionary(hh => hh.Id);
            var sExplorers = (await _context.Explorers
                    .Where(e => sExplorerIds.Contains(e.Id))
                    .ToListAsync())
                .ToDictionary(e => e.Id);
            var sessions = sessionsRaw.Select(s =>
            {
                sHouses.TryGetValue(s.HouseId, out var house);
                sExplorers.TryGetValue(s.ExplorerId, out var explorer);
                return (object)new
                {
                    s.Id,
                    HouseNumber = house?.HouseNumber,
                    FlatName = house?.Flat?.FlatName,
                    ExplorerName = explorer != null ? $"{explorer.FirstName} {explorer.LastName}".Trim() : null,
                    s.ScheduledAt,
                    s.Status,
                    s.AgentRating,
                    s.ClosedAt
                };
            }).ToList();

            // ── Agreement + ID-document status (delegated to IAgreementService) ──
            var agreement = await _agreementService.GetMyAgreementAsync(id);
            var idDocument = await _agreementService.GetMyIdDocumentAsync(id);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                Profile = new
                {
                    agent.Id,
                    agent.Email,
                    agent.FirstName,
                    agent.LastName,
                    agent.PhoneNumber,
                    agent.AgencyName,
                    agent.LicenseNumber,
                    agent.County,
                    agent.Constituency,
                    agent.Ward,
                    agent.IsActive,
                    agent.IsEmailVerified,
                    agent.CreatedAt,
                    agent.UpdatedAt,
                    PortalUserType = agent.PortalUserType.ToString()
                },
                Rating = new { averageRating, ratingCount },
                AssignedFlats = flats,
                EscalatedComplaints = complaints,
                Sessions = new { Total = totalSessions, Recent = sessions },
                Agreement = agreement,
                IdDocument = idDocument
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building agent detail: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the agent detail"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var agents = await _agentService.GetAllAsync();
            var agentIds = agents.Select(a => a.Id).ToList();
            var ratingAggregates = await _context.ListingViewingSessions
                .Where(s => agentIds.Contains(s.AgentId) && s.Status == "Closed" && s.AgentRating != null)
                .GroupBy(s => s.AgentId)
                .Select(g => new { AgentId = g.Key, Avg = g.Average(s => (double)s.AgentRating!.Value), Count = g.Count() })
                .ToListAsync();
            var ratingDict = ratingAggregates.ToDictionary(x => x.AgentId, x => x);

            var data = agents.Select(a =>
            {
                ratingDict.TryGetValue(a.Id, out var r);
                return (object)new
                {
                    a.Id,
                    a.Email,
                    a.FirstName,
                    a.LastName,
                    a.PhoneNumber,
                    a.AgencyName,
                    a.LicenseNumber,
                    a.County,
                    a.Constituency,
                    a.Ward,
                    a.IsActive,
                    a.IsEmailVerified,
                    a.PortalUserType,
                    a.CreatedAt,
                    a.UpdatedAt,
                    averageRating = r != null ? (double?)r.Avg : null,
                    ratingCount = r != null ? r.Count : 0
                };
            }).ToList();

            return Ok(ApiResponse<object>.SuccessResponse(data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all agents");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving agents"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAgentDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var agent = await _agentService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                agent.Id,
                agent.Email,
                agent.FirstName,
                agent.LastName,
                agent.PhoneNumber,
                agent.AgencyName,
                agent.LicenseNumber,
                agent.County,
                agent.Constituency,
                agent.Ward,
                agent.IsActive,
                agent.UpdatedAt
            }, "Agent updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the agent"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _agentService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Agent not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Agent deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the agent"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _agentService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Agent not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Agent status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling agent status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating agent status"));
        }
    }

    [HttpGet("{id}/flats")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetFlats(Guid id)
    {
        var flats = await _context.AgentFlats
            .Include(af => af.Flat)
                .ThenInclude(f => f.Houses)
            .Where(af => af.AgentId == id)
            .Select(af => new
            {
                af.Flat.Id,
                af.Flat.FlatName,
                af.Flat.County,
                af.Flat.Constituency,
                af.Flat.Ward,
                af.AssignedAt
            })
            .ToListAsync();

        return Ok(new { success = true, data = flats });
    }

    [HttpPost("{id}/flats")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> AssignFlats(Guid id, [FromBody] AgentFlatAssignmentDto dto)
    {
        try
        {
            var agent = await _agentService.GetByIdAsync(id);
            if (agent == null)
                return NotFound(ApiResponse<object>.FailureResponse("Agent not found"));

            await _agentService.AssignFlatsAsync(id, dto);
            return Ok(new { success = true, message = "Flats assigned successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning flats to agent: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while assigning flats"));
        }
    }

    [HttpPost("{id:guid}/resend-verification")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> ResendVerificationEmail(Guid id)
    {
        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.Id == id);
        if (agent == null)
            return NotFound(new { success = false, message = "Agent not found." });

        if (agent.IsEmailVerified)
            return BadRequest(new { success = false, message = "This agent has already verified their email." });

        if (string.IsNullOrEmpty(agent.TemporaryInitialPassword))
            return BadRequest(new { success = false, message = "No temporary password on record — cannot resend. Contact support." });

        agent.EmailVerificationToken = Guid.NewGuid().ToString("N");
        agent.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        await _context.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
            agent.EmailVerificationToken, agent.Email, PortalUserType.Agent);

        var emailSent = false;
        for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                    agent.Email, agent.FirstName, verificationLink, agent.TemporaryInitialPassword);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend verification email failed for agent {Email} (attempt {Attempt}/3)", agent.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }

        if (!emailSent)
            return BadRequest(new { success = false, message = "Failed to send verification email after 3 attempts." });

        agent.VerificationEmailSentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Verification email sent." });
    }

    [HttpGet("{id:guid}/ratings")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent")]
    public async Task<IActionResult> GetRatings(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var callerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid) ? cid : Guid.Empty;
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        if (callerRole == "Agent" && callerId != id)
            return Forbid();

        var baseQuery = _context.ListingViewingSessions
            .Where(s => s.AgentId == id && s.Status == "Closed" && s.AgentRating != null);

        var ratingCount = await baseQuery.CountAsync();
        var averageRating = ratingCount > 0
            ? await baseQuery.AverageAsync(s => (double)s.AgentRating!.Value)
            : (double?)null;

        var sessions = await baseQuery
            .OrderByDescending(s => s.ClosedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var explorerIds = sessions.Select(s => s.ExplorerId).Distinct().ToList();

        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id);

        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim());

        var data = sessions.Select(s =>
        {
            houses.TryGetValue(s.HouseId, out var house);
            explorers.TryGetValue(s.ExplorerId, out var explorerName);
            return (object)new
            {
                id = s.Id,
                explorerName,
                rating = s.AgentRating,
                comment = s.ClosingComment,
                houseNumber = house?.HouseNumber,
                flatName = house?.Flat?.FlatName,
                closedAt = s.ClosedAt
            };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total = ratingCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)ratingCount / pageSize),
            averageRating,
            ratingCount
        });
    }
}
