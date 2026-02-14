using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities;

public class Accountant : Admin
{
    public string? LicenseNumber { get; set; }

    public Accountant()
    {
        UserType = UserType.Accountant;
    }
}