namespace ShmsBackend.Data.Models.Entities.Portal;

public class ListingViewingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid ExplorerId { get; set; }
    public Guid AgentId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DeclineReason { get; set; }
    public Guid? ReassignedFromAgentId { get; set; }
    public DateTime? ReassignedAt { get; set; }
    public DateTime? FeedbackPromptSentAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosingComment { get; set; }
    public int? AgentRating { get; set; }
    public string? RescheduleReason { get; set; }
    public DateTime? PreviousScheduledAt { get; set; }
    public int RescheduleCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
