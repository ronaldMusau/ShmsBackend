using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Context;

public class ShmsDbContext : DbContext
{
    public ShmsDbContext(DbContextOptions<ShmsDbContext> options) : base(options)
    {
    }

    // Base Admin table
    public DbSet<Admin> Admins { get; set; }

    // Role-specific tables (Table-per-Type)
    public DbSet<SuperAdmin> SuperAdmins { get; set; }
    public DbSet<AdminUser> AdminUsers { get; set; }
    public DbSet<Manager> Managers { get; set; }
    public DbSet<Accountant> Accountants { get; set; }
    public DbSet<Secretary> Secretaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Admin Configuration (Base Table)
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Remove unique constraint to allow same email for different roles
            // entity.HasIndex(e => e.Email).IsUnique();
            // Add non-unique index for performance
            entity.HasIndex(e => e.Email);

            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsEmailVerified).HasDefaultValue(false);
            entity.Property(e => e.EmailVerificationToken).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UserType).IsRequired();
            entity.Property(e => e.CreatedBy).HasColumnName("CreatedBy");

            // Configure the Creator relationship
            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Table-per-Type mapping
            entity.ToTable("Admins");
        });

        // SuperAdmin Configuration
        modelBuilder.Entity<SuperAdmin>(entity =>
        {
            entity.ToTable("SuperAdmins");
        });

        // AdminUser Configuration
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("AdminUsers");
            entity.Property(e => e.Department).HasMaxLength(100);
        });

        // Manager Configuration
        modelBuilder.Entity<Manager>(entity =>
        {
            entity.ToTable("Managers");
            entity.Property(e => e.ManagedDepartment).HasMaxLength(100);
        });

        // Accountant Configuration
        modelBuilder.Entity<Accountant>(entity =>
        {
            entity.ToTable("Accountants");
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
        });

        // Secretary Configuration
        modelBuilder.Entity<Secretary>(entity =>
        {
            entity.ToTable("Secretaries");
            entity.Property(e => e.OfficeNumber).HasMaxLength(20);
        });
    }
}