namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public Guid LandlordId { get; set; }
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public Guid FlatId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsVoided { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
