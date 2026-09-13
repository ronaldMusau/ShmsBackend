namespace ShmsBackend.Data.Models.Entities.Portal;

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? LandlordId { get; set; }        // null = Management-logged, set = Landlord-logged
    public Guid CreatedByUserId { get; set; }     // audit: actual creator's PortalUser Id
    public Guid? FlatId { get; set; }             // required at controller level for Landlord creation, optional for Management
    public Guid? HouseId { get; set; }            // always optional
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public DateTime ExpenseDate { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
