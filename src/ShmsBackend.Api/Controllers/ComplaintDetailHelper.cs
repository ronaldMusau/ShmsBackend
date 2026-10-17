using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

public static class ComplaintDetailHelper
{
    public static async Task<object> BuildAsync(ShmsDbContext context, Complaint complaint, string viewerRole, Guid? agentViewerId = null)
    {
        var house = await context.Houses.FirstOrDefaultAsync(h => h.Id == complaint.HouseId);
        var flat = await context.Flats.FirstOrDefaultAsync(f => f.Id == complaint.FlatId);
        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        var landlord = await context.Landlords.FirstOrDefaultAsync(l => l.Id == complaint.LandlordId);
        var agent = complaint.EscalatedToAgentId.HasValue
            ? await context.Agents.FirstOrDefaultAsync(a => a.Id == complaint.EscalatedToAgentId.Value)
            : null;
        var complaintType = complaint.ComplaintType ?? await context.ComplaintTypes.FirstOrDefaultAsync(t => t.Id == complaint.ComplaintTypeId);
        var closeHistoryEntry = await context.ComplaintStatusHistory
            .Where(h => h.ComplaintId == complaint.Id && h.ToStatus == "Closed")
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync();
        var reopenHistoryEntry = await context.ComplaintStatusHistory
            .Where(h => h.ComplaintId == complaint.Id && h.FromStatus == "Closed" && h.ToStatus == "UnderReview")
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync();
        var attachments = (complaint.Attachments != null && complaint.Attachments.Count > 0)
            ? (IEnumerable<ComplaintAttachment>)complaint.Attachments
            : await context.ComplaintAttachments.Where(a => a.ComplaintId == complaint.Id).ToListAsync();

        var workAttempts = complaint.WorkAttempts.Count > 0
            ? (IEnumerable<ComplaintWorkAttempt>)complaint.WorkAttempts
            : await context.ComplaintWorkAttempts.Where(w => w.ComplaintId == complaint.Id).ToListAsync();

        var landlordDecisions = complaint.LandlordDecisions.Count > 0
            ? (IEnumerable<ComplaintLandlordDecision>)complaint.LandlordDecisions
            : await context.ComplaintLandlordDecisions.Where(d => d.ComplaintId == complaint.Id).ToListAsync();

        bool withinGracePeriod = false;
        if (viewerRole == "Management" && tenant != null && flat != null)
        {
            var initialPayment = await context.Payments
                .Where(p => p.TenantId == tenant.Id && p.IsInitialPayment == true && p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted)
                .FirstOrDefaultAsync();
            if (initialPayment?.PaidAt != null)
            {
                var monthsSinceStart = ((DateTime.UtcNow.Year - initialPayment.PaidAt.Value.Year) * 12) + DateTime.UtcNow.Month - initialPayment.PaidAt.Value.Month;
                withinGracePeriod = monthsSinceStart < flat.BillableGracePeriodMonths;
            }
        }

        var inspections = complaint.Inspections.Count > 0
            ? (IEnumerable<ComplaintInspection>)complaint.Inspections
            : await context.ComplaintInspections.Where(i => i.ComplaintId == complaint.Id).ToListAsync();
        var inspectionAgentIds = inspections.Select(i => i.AgentId).Distinct().ToList();
        var inspectionAgents = await context.Agents.Where(a => inspectionAgentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.FirstName + " " + a.LastName);
        // An agent only sees their own inspection rounds.
        var visibleInspections = viewerRole == "Agent" && agentViewerId.HasValue
            ? inspections.Where(i => i.AgentId == agentViewerId.Value)
            : inspections;
        List<object> inspectionsResponse = visibleInspections.OrderBy(i => i.RoundNumber).Select(i => (object)new
        {
            i.RoundNumber,
            AgentName = inspectionAgents.TryGetValue(i.AgentId, out var an) ? an : null,
            i.Instructions,
            i.RequestedAt,
            i.ReportNotes,
            i.ReportedAt,
            Attachments = attachments
                .Where(a => a.Stage == "Inspection" && a.AttemptNumber == i.RoundNumber)
                .Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt })
        }).ToList();

        List<object>? workAttemptsResponse = workAttempts.OrderBy(w => w.AttemptNumber).Select(w => (object)new
        {
            w.AttemptNumber,
            w.Notes,
            w.SubmittedAt,
            w.TenantVerdict,
            w.TenantVerdictReason,
            w.TenantVerdictAt,
            Attachments = attachments
                .Where(a => a.Stage == "AgentCompletion" && a.AttemptNumber == w.AttemptNumber)
                .Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt })
        }).ToList();

        List<object>? landlordDecisionHistoryResponse = viewerRole == "Management" || viewerRole == "Landlord"
            ? landlordDecisions.OrderBy(d => d.ApprovalAttemptNumber).Select(d => (object)new
            {
                d.ApprovalAttemptNumber,
                d.Decision,
                d.Notes,
                d.DecidedAt
            }).ToList()
            : null;

        List<object>? approvalHistoryResponse = null;
        if (viewerRole == "Management")
        {
            var approvalActions = await context.ComplaintApprovalActions
                .Where(a => a.ComplaintId == complaint.Id)
                .OrderBy(a => a.AttemptNumber)
                .ThenBy(a => a.StepOrder)
                .ToListAsync();
            var approverIds = approvalActions.Select(a => a.ApproverId).Distinct().ToList();
            var approvers = await ApproverLookup.GetNamesAsync(context, approverIds);
            approvalHistoryResponse = approvalActions.Select(a => (object)new
            {
                a.AttemptNumber,
                a.StepOrder,
                a.Decision,
                a.Notes,
                ApproverName = approvers.TryGetValue(a.ApproverId, out var name) ? name : a.ApproverId.ToString(),
                a.ActionedAt
            }).ToList();
        }

        return new
        {
            complaint.Id,
            complaint.TicketNumber,
            complaint.Description,
            complaint.Status,
            complaint.CreatedAt,
            ComplaintTypeName = complaintType?.Name,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
            HouseNumber = house != null ? house.HouseNumber : "-",
            FlatName = flat != null ? flat.FlatName : "-",
            BillableGracePeriodMonths = flat?.BillableGracePeriodMonths ?? 3,
            IsWithinGracePeriod = viewerRole == "Management" ? (bool?)withinGracePeriod : null,
            LandlordName = landlord != null ? $"{landlord.FirstName} {landlord.LastName}" : "-",
            complaint.IsBillable,
            complaint.BillableTarget,
            complaint.BillableTargetOverrideReason,
            complaint.BillableExplanation,
            BillableAmount = viewerRole == "Agent" ? (decimal?)null : complaint.BillableAmount,
            complaint.ReviewedAt,
            complaint.EscalatedAt,
            complaint.EscalatedToAgentId,
            complaint.CurrentApprovalStepOrder,
            EscalationNotes = viewerRole == "Management" || viewerRole == "Agent" ? complaint.EscalationNotes : null,
            AgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
            complaint.AgentCompletionNotes,
            complaint.AgentCompletedAt,
            complaint.TenantVerificationStatus,
            complaint.TenantRejectionReason,
            complaint.TenantCompletedAt,
            complaint.AgentRedoCount,
            // Tenant-charge payment details are private to tenant + management (never landlord/agent).
            TenantChargeDecision = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargeDecision : null,
            TenantChargeDecidedAt = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargeDecidedAt : null,
            TenantChargeDeclineReason = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargeDeclineReason : null,
            TenantChargePaidAt = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargePaidAt : null,
            TenantChargeReceipt = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargeReceipt : null,
            TenantChargePaidBy = viewerRole == "Management" || viewerRole == "Tenant" ? complaint.TenantChargePaidBy : null,
            complaint.ClosedAt,
            ClosingComment = closeHistoryEntry?.Notes,
            ReopenedAt = reopenHistoryEntry?.ChangedAt,
            complaint.LandlordDecision,
            complaint.LandlordDecisionNotes,
            complaint.FinalDecision,
            complaint.LandlordActionedAt,
            Attachments = attachments.Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt, a.Stage, a.AttemptNumber }),
            WorkAttempts = workAttemptsResponse,
            Inspections = inspectionsResponse,
            LandlordDecisionHistory = landlordDecisionHistoryResponse,
            ApprovalHistory = approvalHistoryResponse,
            complaint.NeedsResubmission,
            LastRejectionReason = viewerRole == "Management" && complaint.NeedsResubmission
                ? (await context.ComplaintApprovalActions
                    .Where(a => a.ComplaintId == complaint.Id && a.Decision == "Rejected")
                    .OrderByDescending(a => a.AttemptNumber)
                    .ThenByDescending(a => a.StepOrder)
                    .FirstOrDefaultAsync())?.Notes
                : null
        };
    }
}
