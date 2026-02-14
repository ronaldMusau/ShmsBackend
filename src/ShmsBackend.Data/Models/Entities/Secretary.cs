using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities;

public class Secretary : Admin
{
    public string? OfficeNumber { get; set; }

    public Secretary()
    {
        UserType = UserType.Secretary;
    }
}