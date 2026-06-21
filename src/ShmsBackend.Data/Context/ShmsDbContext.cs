using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Context;

public class ShmsDbContext : DbContext
{
    public ShmsDbContext(DbContextOptions<ShmsDbContext> options) : base(options)
    {
    }

    // Admin hierarchy (Table-per-Type)
    public DbSet<Admin> Admins { get; set; }
    public DbSet<SuperAdmin> SuperAdmins { get; set; }
    public DbSet<AdminUser> AdminUsers { get; set; }
    public DbSet<Manager> Managers { get; set; }
    public DbSet<Accountant> Accountants { get; set; }
    public DbSet<Secretary> Secretaries { get; set; }

    // Portal user hierarchy (Table-per-Type)
    public DbSet<PortalUser> PortalUsers { get; set; }
    public DbSet<Landlord> Landlords { get; set; }
    public DbSet<Agent> Agents { get; set; }
    public DbSet<Tenant> Tenants { get; set; }

    // Property listings
    public DbSet<House> Houses { get; set; }
    public DbSet<Flat> Flats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Admin Configuration ──────────────────────────────────────────────
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            entity.ToTable("Admins");
        });

        modelBuilder.Entity<SuperAdmin>(entity => entity.ToTable("SuperAdmins"));

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("AdminUsers");
            entity.Property(e => e.Department).HasMaxLength(100);
        });

        modelBuilder.Entity<Manager>(entity =>
        {
            entity.ToTable("Managers");
            entity.Property(e => e.ManagedDepartment).HasMaxLength(100);
        });

        modelBuilder.Entity<Accountant>(entity =>
        {
            entity.ToTable("Accountants");
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
        });

        modelBuilder.Entity<Secretary>(entity =>
        {
            entity.ToTable("Secretaries");
            entity.Property(e => e.OfficeNumber).HasMaxLength(20);
        });

        // ── PortalUser Configuration (Table-per-Type) ────────────────────────
        modelBuilder.Entity<PortalUser>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.Property(e => e.PortalUserType).IsRequired();
            entity.ToTable("PortalUsers");
        });

        modelBuilder.Entity<Landlord>(entity =>
        {
            entity.ToTable("Landlords");
            entity.Property(e => e.NationalId).HasMaxLength(50);
            entity.Property(e => e.AgencyName).HasMaxLength(200);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("Agents");
            entity.Property(e => e.AgencyName).HasMaxLength(200);
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.Property(e => e.EmergencyContactName).HasMaxLength(100);
            entity.Property(e => e.EmergencyContactPhone).HasMaxLength(20);
        });

        // ── House Configuration ──────────────────────────────────────────────
        modelBuilder.Entity<House>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(500);
            entity.Property(e => e.City).IsRequired().HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(100);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            entity.Property(e => e.IsAvailable).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Landlord)
                .WithMany()
                .HasForeignKey(e => e.LandlordId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.ToTable("Houses");
        });

        // ── Flat Configuration ───────────────────────────────────────────────
        modelBuilder.Entity<Flat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(500);
            entity.Property(e => e.City).IsRequired().HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(100);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            entity.Property(e => e.IsAvailable).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.House)
                .WithMany(h => h.Flats)
                .HasForeignKey(e => e.HouseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Landlord)
                .WithMany()
                .HasForeignKey(e => e.LandlordId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Agent)
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.ToTable("Flats");
        });
    }
}
