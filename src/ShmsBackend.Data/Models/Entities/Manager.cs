using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities;

public class Manager : Admin
{
    public string? ManagedDepartment { get; set; }
    public int TeamSize { get; set; } = 0;

    public Manager()
    {
        UserType = UserType.Manager;
    }
}