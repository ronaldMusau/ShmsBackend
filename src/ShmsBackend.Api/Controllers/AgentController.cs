using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Api.Models.Responses;
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

    public AgentController(
        IAgentService agentService,
        ILogger<AgentController> logger,
        ShmsDbContext context,
        IFrontendUrlService frontendUrlService,
        IEmailService emailService)
    {
        _agentService = agentService;
        _logger = logger;
        _context = context;
        _frontendUrlService = frontendUrlService;
        _emailService = emailService;
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
                agent.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting agent: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the agent"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var agents = await _agentService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(agents));
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
}
