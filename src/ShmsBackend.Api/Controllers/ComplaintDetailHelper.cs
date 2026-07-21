using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

public static class ComplaintDetailHelper
{
    public static async Task<object> BuildAsync(ShmsDbContext context, Complaint complaint, string viewerRole)
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
        var attachments = (complaint.Attachments != null && complaint.Attachments.Count > 0)
            ? (IEnumerable<ComplaintAttachment>)complaint.Attachments
            : await context.ComplaintAttachments.Where(a => a.ComplaintId == complaint.Id).ToListAsync();

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
            LandlordName = landlord != null ? $"{landlord.FirstName} {landlord.LastName}" : "-",
            complaint.IsBillable,
            complaint.BillableTarget,
            complaint.BillableTargetOverrideReason,
            complaint.BillableExplanation,
            BillableAmount = viewerRole == "Agent" ? (decimal?)null : complaint.BillableAmount,
            complaint.ReviewedAt,
            complaint.EscalatedAt,
            complaint.EscalationNotes,
            AgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
            complaint.AgentCompletionNotes,
            complaint.AgentCompletedAt,
            complaint.TenantVerificationStatus,
            complaint.TenantRejectionReason,
            complaint.TenantCompletedAt,
            complaint.AgentRedoCount,
            complaint.ClosedAt,
            ClosingComment = closeHistoryEntry?.Notes,
            Attachments = attachments.Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt, a.Stage })
        };
    }
}
