using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities;

public class SuperAdmin : Admin
{
    public string? SuperAdminPermissions { get; set; } = "full_access";

    public SuperAdmin()
    {
        UserType = UserType.SuperAdmin;
    }
}