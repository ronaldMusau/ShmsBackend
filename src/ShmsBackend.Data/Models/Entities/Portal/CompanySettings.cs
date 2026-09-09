using System;

namespace ShmsBackend.Data.Models.Entities.Portal
{
    public class CompanySettings
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? CompanyName { get; set; }
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? LogoPath { get; set; }
        public Guid? UpdatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
