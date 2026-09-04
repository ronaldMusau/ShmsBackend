using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class TenantWarning : ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public int WarningNumber { get; set; } // 1 or 2
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public Guid SentByAdminId { get; set; }
    public decimal ArrearsAtTime { get; set; }
    public int OverdueDaysAtTime { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
