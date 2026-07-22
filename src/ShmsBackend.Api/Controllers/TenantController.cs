using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
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
public class TenantController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ILogger<TenantController> _logger;
    private readonly ShmsDbContext _context;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IEmailService _emailService;

    public TenantController(
        ITenantService tenantService,
        ILogger<TenantController> logger,
        ShmsDbContext context,
        IFrontendUrlService frontendUrlService,
        IEmailService emailService)
    {
        _tenantService = tenantService;
        _logger = logger;
        _context = context;
        _frontendUrlService = frontendUrlService;
        _emailService = emailService;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = tenant.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    tenant.Id,
                    tenant.Email,
                    tenant.FirstName,
                    tenant.LastName,
                    tenant.PhoneNumber,
                    tenant.DateOfBirth,
                    tenant.EmergencyContactName,
                    tenant.EmergencyContactPhone,
                    tenant.IsActive,
                    tenant.PortalUserType
                }, "Tenant created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the tenant"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var tenant = await _tenantService.GetByIdAsync(id);
            if (tenant == null)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.IsEmailVerified,
                tenant.PortalUserType,
                tenant.CreatedAt,
                tenant.UpdatedAt,
                tenant.NationalId,
                tenant.County,
                tenant.Constituency,
                tenant.Ward,
                TenantStatus = tenant.TenantStatus.ToString()
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the tenant"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var tenants = (await _tenantService.GetAllAsync()).ToList();
            var total = tenants.Count;
            var pagedTenants = tenants.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // For any house whose flat was soft-deleted, look up the real name
            var deletedFlatIds = pagedTenants
                .Where(t => t.House != null && t.House.Flat == null)
                .Select(t => t.House!.FlatId)
                .Distinct()
                .ToList();

            var deletedFlatNames = new Dictionary<Guid, string>();
            if (deletedFlatIds.Count > 0)
            {
                deletedFlatNames = await _context.Flats
                    .IgnoreQueryFilters()
                    .Where(f => deletedFlatIds.Contains(f.Id))
                    .ToDictionaryAsync(f => f.Id, f => f.FlatName);
            }

            var data = pagedTenants.Select(t => new
            {
                t.Id,
                t.Email,
                t.FirstName,
                t.LastName,
                t.PhoneNumber,
                t.IsActive,
                t.IsEmailVerified,
                TenantStatus = t.TenantStatus.ToString(),
                Status = t.TenantStatus.ToString(),
                t.HasCompletedInitialPayment,
                t.HouseId,
                HouseNumber = t.House != null ? t.House.HouseNumber : null,
                HouseName = t.House != null
                    ? (t.House.Flat != null
                        ? $"{t.House.HouseNumber} - {t.House.Flat.FlatName}"
                        : (deletedFlatNames.TryGetValue(t.House.FlatId, out var fn)
                            ? $"{t.House.HouseNumber} - {fn}"
                            : $"{t.House.HouseNumber} - (Flat Deleted)"))
                    : null,
                t.CreatedAt,
                t.NationalId,
                t.County,
                t.Constituency,
                t.Ward,
                t.DateOfBirth,
                t.EmergencyContactName,
                t.EmergencyContactPhone,
                t.UpdatedAt,
                PortalUserType = t.PortalUserType.ToString()
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tenants");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving tenants"));
        }
    }

    [HttpPut("{id}")]
    [HttpPatch("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.UpdatedAt
            }, "Tenant updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the tenant"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _tenantService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the tenant"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _tenantService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling tenant status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating tenant status"));
        }
    }

    [HttpPost("{id:guid}/resend-verification")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Agent")]
    public async Task<IActionResult> ResendVerificationEmail(Guid id)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (User.IsInRole("Agent"))
        {
            var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(agentIdStr, out var agentId))
                return Unauthorized();

            var flatId = tenant.House?.FlatId;
            if (flatId == null)
                return StatusCode(403, new { success = false, message = "You are not authorized to resend verification for this tenant." });

            var authorized = await _context.AgentFlats
                .AnyAsync(af => af.AgentId == agentId && af.FlatId == flatId);
            if (!authorized)
                return StatusCode(403, new { success = false, message = "You are not authorized to resend verification for this tenant." });
        }

        if (!tenant.HasCompletedInitialPayment)
            return BadRequest(new { success = false, message = "This tenant has not completed their initial payment yet." });

        if (tenant.IsEmailVerified)
            return BadRequest(new { success = false, message = "This tenant has already verified their email." });

        if (string.IsNullOrEmpty(tenant.TemporaryInitialPassword))
            return BadRequest(new { success = false, message = "No temporary password on record — cannot resend. Contact support." });

        tenant.EmailVerificationToken = Guid.NewGuid().ToString("N");
        tenant.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        await _context.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
            tenant.EmailVerificationToken, tenant.Email, PortalUserType.Tenant);

        var emailSent = false;
        for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                    tenant.Email, tenant.FirstName, verificationLink, tenant.TemporaryInitialPassword);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend verification email failed for tenant {Email} (attempt {Attempt}/3)", tenant.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }

        if (!emailSent)
            return BadRequest(new { success = false, message = "Failed to send verification email after 3 attempts." });

        tenant.VerificationEmailSentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Verification email sent." });
    }
}
