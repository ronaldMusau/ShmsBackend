namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateForfeitedAdvance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public Guid FlatId { get; set; }
    public Guid LandlordId { get; set; }
    public decimal TotalAdvanceAmount { get; set; }
    public decimal AmountAppliedToDamages { get; set; }
    public decimal AmountForfeitedUnused { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
