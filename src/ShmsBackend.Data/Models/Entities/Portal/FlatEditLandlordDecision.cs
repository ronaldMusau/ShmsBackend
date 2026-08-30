namespace ShmsBackend.Data.Models.Entities.Portal;

public class FlatEditLandlordDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlatEditRequestId { get; set; }
    public FlatEditRequest? FlatEditRequest { get; set; }
    public int ApprovalAttemptNumber { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime DecidedAt { get; set; }
    public Guid DecidedByLandlordId { get; set; }
}
