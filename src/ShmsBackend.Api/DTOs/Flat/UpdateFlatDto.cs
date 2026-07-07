namespace ShmsBackend.Api.Models.DTOs.Flat;

public class UpdateFlatDto
{
    public string? FlatName { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public Guid? AgentId { get; set; }
}
