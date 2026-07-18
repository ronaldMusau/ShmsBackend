namespace ShmsBackend.Data.Models.Entities.Portal;

public class Deduction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LandlordId { get; set; }
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public Guid FlatId { get; set; }
    public Guid ComplaintId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public int DeductionMonth { get; set; }
    public int DeductionYear { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
