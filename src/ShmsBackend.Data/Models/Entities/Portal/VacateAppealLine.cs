namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateAppealLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateAppealId { get; set; }
    public VacateAppeal? VacateAppeal { get; set; }
    public Guid VacateInspectionLineId { get; set; }
    public VacateInspectionLine? VacateInspectionLine { get; set; }
    public string Reason { get; set; } = string.Empty;
}
