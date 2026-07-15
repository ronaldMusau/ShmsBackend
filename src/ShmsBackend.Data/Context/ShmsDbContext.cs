using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Interfaces;

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
    public DbSet<Explorer> Explorers { get; set; }

    // Property listings
    public DbSet<House> Houses { get; set; }
    public DbSet<Flat> Flats { get; set; }
    public DbSet<HouseImage> HouseImages { get; set; }
    public DbSet<PendingRentChange> PendingRentChanges { get; set; }

    // Agent–Flat assignments
    public DbSet<AgentFlat> AgentFlats { get; set; }

    // Notifications
    public DbSet<Notification> Notifications { get; set; }

    // Tenant house history
    public DbSet<TenantHouseHistory> TenantHouseHistories { get; set; }

    // Payments
    public DbSet<Payment> Payments { get; set; }
    public DbSet<PaymentApplication> PaymentApplications { get; set; }
    public DbSet<PaymentCheckoutAttempt> PaymentCheckoutAttempts { get; set; }
    public DbSet<ServiceChargeSetting> ServiceChargeSettings { get; set; }

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
            entity.Property(e => e.DateOfBirth);
            entity.Property(e => e.NationalId).HasMaxLength(50);
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
            entity.Property(e => e.DateOfBirth);
            entity.Property(e => e.NationalId).HasMaxLength(50);
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
            entity.HasOne(e => e.House)
                  .WithMany(h => h.Tenants)
                  .HasForeignKey(e => e.HouseId)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        });

        modelBuilder.Entity<Explorer>(entity =>
        {
            entity.ToTable("Explorers");
        });

        // ── Flat Configuration ───────────────────────────────────────────────
        modelBuilder.Entity<Flat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FlatName).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.FlatName).IsUnique();
            entity.Property(e => e.County).HasMaxLength(100);
            entity.Property(e => e.Constituency).HasMaxLength(100);
            entity.Property(e => e.Ward).HasMaxLength(100);
            entity.HasOne(e => e.Landlord)
                  .WithMany()
                  .HasForeignKey(e => e.LandlordId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Houses)
                  .WithOne(h => h.Flat)
                  .HasForeignKey(h => h.FlatId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── House Configuration ──────────────────────────────────────────────
        modelBuilder.Entity<House>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HouseNumber).IsRequired().HasMaxLength(20);
            entity.Property(e => e.HouseType).IsRequired()
                  .HasConversion<string>();
            entity.Property(e => e.RentFee).HasColumnType("decimal(10,2)");
            entity.Property(e => e.DepositFee).HasColumnType("decimal(10,2)");
            entity.Property(e => e.OccupancyStatus)
                  .HasConversion<string>()
                  .HasDefaultValue(OccupancyStatus.Vacant);
            entity.Property(e => e.PaymentStatus)
                  .HasConversion<string>()
                  .HasDefaultValue(PaymentStatus.NotPaid)
                  .HasSentinel(PaymentStatus.NotPaid);
        });

        // ── HouseImage Configuration ─────────────────────────────────────────
        modelBuilder.Entity<HouseImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ImagePath).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.House)
                  .WithMany(h => h.Images)
                  .HasForeignKey(e => e.HouseId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MpesaReceiptNumber).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AmountApplied).HasColumnType("decimal(18,2)");
            entity.HasOne(e => e.Payment)
                  .WithMany(p => p.Applications)
                  .HasForeignKey(e => e.PaymentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentCheckoutAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CheckoutRequestId).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Payment)
                  .WithMany()
                  .HasForeignKey(e => e.PaymentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PendingRentChange Configuration ─────────────────────────────────
        modelBuilder.Entity<PendingRentChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NewRentFee).HasColumnType("decimal(10,2)");
            entity.Property(e => e.NewDepositFee).HasColumnType("decimal(10,2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasOne(e => e.House)
                  .WithMany()
                  .HasForeignKey(e => e.HouseId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AgentFlat Configuration ──────────────────────────────────────────
        modelBuilder.Entity<AgentFlat>(entity =>
        {
            entity.HasKey(e => new { e.AgentId, e.FlatId });
            entity.HasOne(e => e.Agent)
                  .WithMany(a => a.AgentFlats)
                  .HasForeignKey(e => e.AgentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Flat)
                  .WithMany(f => f.AgentFlats)
                  .HasForeignKey(e => e.FlatId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.AssignedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.ToTable("AgentFlats");
        });

        // ── Notification Configuration ───────────────────────────────────────
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(50).HasDefaultValue("general");
            entity.Property(e => e.TargetUserId).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasIndex(e => e.TargetUserId);
            entity.HasIndex(e => e.Audience);
            entity.HasIndex(e => e.IsRead);
            entity.ToTable("Notifications");
        });

        // ── TenantHouseHistory Configuration ────────────────────────────────
        modelBuilder.Entity<TenantHouseHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.House)
                .WithMany()
                .HasForeignKey(e => e.HouseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.TenantFirstName).HasMaxLength(100);
            entity.Property(e => e.TenantLastName).HasMaxLength(100);
            entity.Property(e => e.TenantEmail).HasMaxLength(255);
            entity.Property(e => e.TenantPhone).HasMaxLength(20);
            entity.Property(e => e.HouseNumber).HasMaxLength(50);
            entity.Property(e => e.FlatName).HasMaxLength(200);
        });

        // ── Payment Configuration ────────────────────────────────────────────
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.AmountPaid).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Balance).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RentAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.DepositAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ServiceChargeAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.CreditApplied).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RequestedDistributionAmount).HasColumnType("decimal(18,2)");
            entity.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.House).WithMany().HasForeignKey(e => e.HouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Flat).WithMany().HasForeignKey(e => e.FlatId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── ServiceChargeSetting Configuration ──────────────────────────────
        modelBuilder.Entity<ServiceChargeSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MinRent).HasColumnType("decimal(18,2)");
            entity.Property(e => e.MaxRent).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ServiceCharge).HasColumnType("decimal(18,2)");
        });

        // ── Global soft-delete filters ───────────────────────────────────────
        modelBuilder.Entity<PortalUser>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Admin>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Flat>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<House>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Notification>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<ServiceChargeSetting>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Payment>().HasQueryFilter(e => !e.IsDeleted);
    }
}
