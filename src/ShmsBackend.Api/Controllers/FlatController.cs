using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/flats")]
public class FlatController : ControllerBase
{
    private readonly FlatService _flatService;
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<FlatController> _logger;

    public FlatController(
        FlatService flatService,
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<FlatController> logger)
    {
        _flatService = flatService;
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateFlatDto dto)
    {
        try
        {
            var result = await _flatService.CreateAsync(dto);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create flat: {Message}", ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _flatService.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _flatService.GetByIdAsync(id);
        if (result == null) return NotFound(new { success = false, message = "Flat not found." });
        return Ok(new { success = true, data = result });
    }

    [HttpGet("landlord/{landlordId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetByLandlord(Guid landlordId)
    {
        var result = await _flatService.GetByLandlordAsync(landlordId);
        return Ok(new { success = true, data = result });
    }

    [HttpGet("location")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetByLocation(
        [FromQuery] string county,
        [FromQuery] string constituency,
        [FromQuery] string ward)
    {
        var result = await _flatService.GetByLocationAsync(county, constituency, ward);
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFlatDto dto)
    {
        try
        {
            var result = await _flatService.UpdateAsync(id, dto);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _flatService.DeleteAsync(id);
        if (!result) return NotFound(new { success = false, message = "Flat not found." });
        return Ok(new { success = true, message = "Flat deleted successfully." });
    }

    [HttpPost("{flatId:guid}/houses")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> AddHouseLines(Guid flatId, [FromBody] List<HouseGroupDto> houseLines)
    {
        try
        {
            var result = await _flatService.AddHouseLinesAsync(flatId, houseLines);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add house lines to flat {FlatId}: {Message}", flatId, ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{flatId:guid}/house-types/{houseTypeId:guid}/increase-count")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> IncreaseHouseTypeCount(Guid flatId, Guid houseTypeId, [FromBody] IncreaseHouseCountDto dto)
    {
        try
        {
            var result = await _flatService.IncreaseHouseCountAsync(flatId, houseTypeId, dto.AdditionalCount);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increase house count for flat {FlatId}, house type {HouseTypeId}: {Message}", flatId, houseTypeId, ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPut("{flatId:guid}/house-types/{houseTypeId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> EditHouseTypeGroup(Guid flatId, Guid houseTypeId, [FromBody] EditHouseGroupDto dto)
    {
        try
        {
            var result = await _flatService.EditHouseGroupAsync(flatId, houseTypeId, dto);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit house type group for flat {FlatId}, house type {HouseTypeId}: {Message}", flatId, houseTypeId, ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{flatId:guid}/house-types/{houseTypeId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> DeleteHouseTypeGroup(Guid flatId, Guid houseTypeId)
    {
        try
        {
            var result = await _flatService.DeleteHouseGroupAsync(flatId, houseTypeId);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, message = "House type group deleted." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete house type group for flat {FlatId}, house type {HouseTypeId}: {Message}", flatId, houseTypeId, ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // POST /api/flats/{id}/edit-request
    [HttpPost("{id:guid}/edit-request")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> SubmitEditRequest(Guid id, [FromBody] UpdateFlatDto dto)
    {
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == id);
        if (flat == null) return NotFound(new { success = false, message = "Flat not found." });

        var existingPending = await _context.FlatEditRequests.AnyAsync(r => r.FlatId == id && r.Status == "Pending");
        if (existingPending)
            return BadRequest(new { success = false, message = "This flat already has a pending edit request awaiting approval." });

        if (string.IsNullOrWhiteSpace(dto.SubmissionNotes))
            return BadRequest(new { success = false, message = "A reason for this edit is required." });

        var firstStep = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "FlatEdit")
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync();
        if (firstStep == null)
            return BadRequest(new { success = false, errorCode = "NO_APPROVAL_SEQUENCE", message = "No approval sequence is configured for Flat Edits yet. Set one up under Setups > Approvals before proceeding." });

        if (dto.HouseTypeChanges != null && dto.HouseTypeChanges.Count > 0)
        {
            var missingDeleteReason = dto.HouseTypeChanges
                .Any(c => c.ActionType == "Delete" && string.IsNullOrWhiteSpace(c.DeleteReason));
            if (missingDeleteReason)
                return BadRequest(new { success = false, message = "A reason is required for every house type group being deleted." });

            var missingEffectiveDate = dto.HouseTypeChanges
                .Any(c => c.ActionType == "ScheduleRentChange" && (!c.EffectiveMonth.HasValue || !c.EffectiveYear.HasValue));
            if (missingEffectiveDate)
                return BadRequest(new { success = false, message = "An effective month is required for every scheduled price change." });
        }

        var adminId = GetUserId();
        var request = new FlatEditRequest
        {
            Id = Guid.NewGuid(),
            FlatId = id,
            ProposedFlatName = dto.FlatName,
            ProposedCounty = dto.County,
            ProposedConstituency = dto.Constituency,
            ProposedWard = dto.Ward,
            ProposedRentDueDay = dto.RentDueDay,
            ProposedBillableGracePeriodMonths = dto.BillableGracePeriodMonths,
            ProposedVacateNoticeDeadlineDay = dto.VacateNoticeDeadlineDay,
            ProposedSitDeposit = dto.SitDeposit,
            ProposedGoogleMapsLink = dto.GoogleMapsLink,
            ProposedAgentId = dto.AgentId,
            ClearAgent = dto.ClearAgent,
            SubmissionNotes = dto.SubmissionNotes,
            RequestedByUserId = adminId,
            Status = "Pending",
            CurrentApprovalStepOrder = firstStep.StepOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.FlatEditRequests.Add(request);

        if (dto.HouseTypeChanges != null)
        {
            foreach (var change in dto.HouseTypeChanges)
            {
                _context.FlatEditHouseTypeChanges.Add(new FlatEditHouseTypeChange
                {
                    Id = Guid.NewGuid(),
                    FlatEditRequestId = request.Id,
                    ActionType = change.ActionType,
                    HouseTypeId = change.HouseTypeId,
                    ProposedPrefix = change.Prefix,
                    ProposedRentFee = change.RentFee,
                    ProposedDepositFee = change.DepositFee,
                    ProposedCount = change.Count,
                    AdditionalCount = change.AdditionalCount,
                    DeleteReason = change.DeleteReason,
                    ProposedEffectiveMonth = change.EffectiveMonth,
                    ProposedEffectiveYear = change.EffectiveYear,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();

        var landlord = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == flat.LandlordId);
        if (landlord != null)
        {
            try { await _notificationService.SendToUserAsync(landlord.Id.ToString(), $"An edit has been submitted for your flat \"{flat.FlatName}\" and sent for approval.", "property", "FlatEdit", flat.Id.ToString()); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of flat edit submission"); }
            try { await _emailService.SendFlatEditSubmittedEmailAsync(landlord.Email, landlord.FirstName, flat.FlatName, landlord.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send flat edit submission email"); }
        }

        var firstApprover = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == firstStep.ApproverId);
        if (firstApprover != null)
        {
            try { await _notificationService.SendToUserAsync(firstApprover.Id.ToString(), $"A flat edit for \"{flat.FlatName}\" requires your approval.", "property", "FlatEdit", flat.Id.ToString()); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify first approver of flat edit"); }
            try { await _emailService.SendApprovalStepEmailAsync(firstApprover.Email, firstApprover.FirstName, $"FLAT-{flat.FlatName}", firstStep.StepOrder, firstApprover.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send flat edit approval-step email"); }
        }

        return Ok(new { success = true, message = "Edit submitted for approval.", requestId = request.Id });
    }

    // PATCH /api/flats/edit-request/{id}/approval-action
    [HttpPatch("edit-request/{id:guid}/approval-action")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> EditRequestApprovalAction(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var request = await _context.FlatEditRequests.Include(r => r.Flat).FirstOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound(new { success = false, message = "Edit request not found." });

        var adminId = GetUserId();
        var steps = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "FlatEdit")
            .OrderBy(s => s.StepOrder)
            .ToListAsync();
        var currentStep = steps.FirstOrDefault(s => s.StepOrder == request.CurrentApprovalStepOrder);
        if (currentStep == null) return BadRequest(new { success = false, message = "This edit request is not currently awaiting an internal approval step." });
        if (currentStep.ApproverId != adminId) return Forbid();

        _context.FlatEditApprovalActions.Add(new FlatEditApprovalAction
        {
            Id = Guid.NewGuid(),
            FlatEditRequestId = request.Id,
            AttemptNumber = request.ApprovalAttemptNumber,
            StepOrder = currentStep.StepOrder,
            ApproverId = adminId,
            Decision = dto.Approved ? "Approved" : "Rejected",
            Notes = dto.Notes,
            ActionedAt = DateTime.UtcNow
        });

        if (!dto.Approved)
        {
            if (string.IsNullOrWhiteSpace(dto.Notes)) return BadRequest(new { success = false, message = "Rejection notes are required." });

            request.Status = "Pending";
            request.CurrentApprovalStepOrder = steps.First().StepOrder;
            request.ApprovalAttemptNumber += 1;
            request.RejectionReason = dto.Notes;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var requester = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == request.RequestedByUserId);
            if (requester != null)
            {
                try { await _notificationService.SendToUserAsync(requester.Id.ToString(), $"Your flat edit for \"{request.Flat!.FlatName}\" was rejected and needs revision.", "property", "FlatEdit", request.FlatId.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify requester of flat edit rejection"); }
                try { await _emailService.SendApprovalRejectedEmailAsync(requester.Email, requester.FirstName, $"FLAT-{request.Flat!.FlatName}", dto.Notes!, requester.Id.ToString(), true); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send flat edit rejection email"); }
            }
            return Ok(new { success = true, message = "Rejected. Sent back to the requester for revision." });
        }

        var nextStep = steps.FirstOrDefault(s => s.StepOrder > currentStep.StepOrder);
        if (nextStep != null)
        {
            request.CurrentApprovalStepOrder = nextStep.StepOrder;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var nextApprover = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == nextStep.ApproverId);
            if (nextApprover != null)
            {
                try { await _notificationService.SendToUserAsync(nextApprover.Id.ToString(), $"A flat edit for \"{request.Flat!.FlatName}\" requires your approval.", "property", "FlatEdit", request.FlatId.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify next approver of flat edit"); }
                try { await _emailService.SendApprovalStepEmailAsync(nextApprover.Email, nextApprover.FirstName, $"FLAT-{request.Flat!.FlatName}", nextStep.StepOrder, nextApprover.Id.ToString(), true); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send flat edit approval-step email"); }
            }
            return Ok(new { success = true, message = "Approved. Advanced to the next approval step." });
        }
        else
        {
            request.CurrentApprovalStepOrder = null;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var editFlat = request.Flat!;
            var landlord = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == editFlat.LandlordId);
            if (landlord != null)
            {
                try { await _notificationService.SendToUserAsync(landlord.Id.ToString(), $"An edit to your flat \"{editFlat.FlatName}\" requires your final approval.", "property", "FlatEdit", editFlat.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of pending flat edit approval"); }
                try { await _emailService.SendFlatEditApprovalNeededEmailAsync(landlord.Email, landlord.FirstName, editFlat.FlatName, landlord.Id.ToString(), true); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send landlord flat edit approval-needed email"); }
            }
            return Ok(new { success = true, message = "Approved. Internal sequence complete — sent to landlord for final approval." });
        }
    }

    // GET /api/flats/edit-request/my-queue
    [HttpGet("edit-request/my-queue")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetMyEditRequestQueue()
    {
        var adminId = GetUserId();
        var myStepOrders = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "FlatEdit" && s.ApproverId == adminId)
            .Select(s => s.StepOrder)
            .ToListAsync();

        var requests = await _context.FlatEditRequests
            .Include(r => r.Flat)
            .Include(r => r.HouseTypeChanges)
            .Where(r => r.CurrentApprovalStepOrder != null && myStepOrders.Contains(r.CurrentApprovalStepOrder.Value))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var houseTypeIds = requests
            .SelectMany(r => r.HouseTypeChanges)
            .Select(c => c.HouseTypeId)
            .Distinct()
            .ToList();
        var houseTypeNames = await _context.HouseTypes
            .Where(t => houseTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var requesterIds = requests.Select(r => r.RequestedByUserId).Distinct().ToList();
        var requesters = await _context.Admins
            .Where(a => requesterIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}");

        var proposedAgentIds = requests
            .Where(r => r.ProposedAgentId.HasValue)
            .Select(r => r.ProposedAgentId!.Value)
            .Distinct()
            .ToList();
        var proposedAgents = await _context.Agents
            .Where(a => proposedAgentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}");

        var flatIds = requests.Select(r => r.FlatId).Distinct().ToList();
        var currentAgentAssignments = await _context.AgentFlats
            .Where(af => flatIds.Contains(af.FlatId))
            .Include(af => af.Agent)
            .ToListAsync();
        var currentAgentByFlat = currentAgentAssignments
            .GroupBy(af => af.FlatId)
            .ToDictionary(
                g => g.Key,
                g => g.First().Agent != null ? $"{g.First().Agent!.FirstName} {g.First().Agent!.LastName}" : (string?)null);

        var data = requests.Select(r => new
        {
            r.Id,
            r.FlatId,
            FlatName = r.Flat!.FlatName,
            r.ProposedFlatName,
            r.ProposedCounty,
            r.ProposedConstituency,
            r.ProposedWard,
            r.ProposedRentDueDay,
            r.ProposedBillableGracePeriodMonths,
            r.ProposedVacateNoticeDeadlineDay,
            r.ProposedSitDeposit,
            r.ProposedGoogleMapsLink,
            r.RequestedByUserId,
            r.CreatedAt,
            RequestedByName = requesters.GetValueOrDefault(r.RequestedByUserId, "Unknown"),
            CurrentFlatName = r.Flat.FlatName,
            CurrentCounty = r.Flat.County,
            CurrentConstituency = r.Flat.Constituency,
            CurrentWard = r.Flat.Ward,
            CurrentRentDueDay = r.Flat.RentDueDay,
            CurrentBillableGracePeriodMonths = r.Flat.BillableGracePeriodMonths,
            CurrentVacateNoticeDeadlineDay = r.Flat.VacateNoticeDeadlineDay,
            CurrentSitDeposit = r.Flat.SitDeposit,
            CurrentGoogleMapsLink = r.Flat.GoogleMapsLink,
            CurrentAgentName = currentAgentByFlat.GetValueOrDefault(r.FlatId),
            ProposedAgentName = r.ProposedAgentId.HasValue ? proposedAgents.GetValueOrDefault(r.ProposedAgentId.Value) : null,
            r.ClearAgent,
            r.SubmissionNotes,
            HouseTypeChanges = r.HouseTypeChanges.Select(c => new
            {
                c.Id,
                c.ActionType,
                c.HouseTypeId,
                HouseTypeName = houseTypeNames.GetValueOrDefault(c.HouseTypeId, "Unknown"),
                c.ProposedPrefix,
                c.ProposedRentFee,
                c.ProposedDepositFee,
                c.ProposedCount,
                c.AdditionalCount,
                c.DeleteReason,
                c.ProposedEffectiveMonth,
                c.ProposedEffectiveYear
            }).ToList()
        });

        return Ok(new { success = true, requests = data });
    }

    // GET /api/flats/edit-request/my-approval-history
    [HttpGet("edit-request/my-approval-history")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetMyEditRequestApprovalHistory()
    {
        var adminId = GetUserId();

        var actions = await _context.FlatEditApprovalActions
            .Where(a => a.ApproverId == adminId && (a.Decision == "Approved" || a.Decision == "Rejected"))
            .OrderByDescending(a => a.ActionedAt)
            .ToListAsync();

        var requestIds = actions.Select(a => a.FlatEditRequestId).Distinct().ToList();
        var requests = await _context.FlatEditRequests
            .Include(r => r.Flat)
            .Where(r => requestIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r);

        var data = actions.Select(a =>
        {
            requests.TryGetValue(a.FlatEditRequestId, out var r);
            return new
            {
                a.Id,
                flatEditRequestId = a.FlatEditRequestId,
                flatId = r?.FlatId,
                flatName = r?.Flat?.FlatName,
                a.AttemptNumber,
                a.StepOrder,
                decision = a.Decision,
                notes = a.Notes,
                actionedAt = a.ActionedAt
            };
        }).ToList();

        return Ok(new { success = true, history = data });
    }
}
