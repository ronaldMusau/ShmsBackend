using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities;

public class AdminUser : Admin
{
    public string? Department { get; set; }

    public AdminUser()
    {
        UserType = UserType.Admin;
    }
}