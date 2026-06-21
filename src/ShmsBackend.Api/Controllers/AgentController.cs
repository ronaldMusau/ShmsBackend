using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Agent;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;
    private readonly ILogger<AgentController> _logger;

    public AgentController(IAgentService agentService, ILogger<AgentController> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while creating the agent"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving the agent"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving agents"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating the agent"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin")]
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while deleting the agent"));
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
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating agent status"));
        }
    }
}
