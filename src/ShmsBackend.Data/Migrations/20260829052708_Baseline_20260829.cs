using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class Baseline_20260829 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Admins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsEmailVerified = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    EmailVerificationToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmailVerificationTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserType = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Admins_Admins_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSequenceSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSequenceSteps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ReminderDays = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeductionMonth = table.Column<int>(type: "int", nullable: false),
                    DeductionYear = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deductions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPortalUser = table.Column<bool>(type: "bit", nullable: false),
                    MasterEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    MasterInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RentEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RentInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ComplaintsEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ComplaintsInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ApprovalsEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ApprovalsInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PropertiesEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PropertiesInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AccountEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AccountInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TeamActivityEmailEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TeamActivityInAppEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Audience = table.Column<int>(type: "int", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "general"),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    County = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Constituency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ward = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsEmailVerified = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PendingEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    EmailVerificationToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmailVerificationTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    PortalUserType = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceChargeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinRent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxRent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ServiceCharge = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceChargeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TermsAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TermsAndConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAndConditions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TermsHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VacateRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NeedsResubmission = table.Column<bool>(type: "bit", nullable: false),
                    CurrentApprovalStepOrder = table.Column<int>(type: "int", nullable: true),
                    ApprovalAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    VacateMonth = table.Column<int>(type: "int", nullable: false),
                    VacateYear = table.Column<int>(type: "int", nullable: false),
                    SitDeposit = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InspectionAssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InspectionSubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalRemarksAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalRemarksByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accountants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accountants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accountants_Admins_Id",
                        column: x => x.Id,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdminUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminUsers_Admins_Id",
                        column: x => x.Id,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Managers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManagedDepartment = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TeamSize = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Managers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Managers_Admins_Id",
                        column: x => x.Id,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Secretaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficeNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Secretaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Secretaries_Admins_Id",
                        column: x => x.Id,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuperAdmins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuperAdminPermissions = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuperAdmins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuperAdmins_Admins_Id",
                        column: x => x.Id,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsBillable = table.Column<bool>(type: "bit", nullable: true),
                    BillableTarget = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillableTargetOverrideReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BillableExplanation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentApprovalStepOrder = table.Column<int>(type: "int", nullable: true),
                    ApprovalAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    LandlordDecision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LandlordDecisionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LandlordActionedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinalDecision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinalDecisionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EscalatedToAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EscalationNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AgentCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AgentCompletionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantVerificationStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantRejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgentRedoCount = table.Column<int>(type: "int", nullable: false),
                    NeedsResubmission = table.Column<bool>(type: "bit", nullable: false),
                    ClosedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Complaints_ComplaintTypes_ComplaintTypeId",
                        column: x => x.ComplaintTypeId,
                        principalTable: "ComplaintTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LicenseNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TemporaryInitialPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationEmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Agents_PortalUsers_Id",
                        column: x => x.Id,
                        principalTable: "PortalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Explorers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Explorers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Explorers_PortalUsers_Id",
                        column: x => x.Id,
                        principalTable: "PortalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Landlords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TemporaryInitialPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationEmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Landlords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Landlords_PortalUsers_Id",
                        column: x => x.Id,
                        principalTable: "PortalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateAppeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateAppeals_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateApprovalActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateApprovalActions_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateForfeitedAdvances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalAdvanceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountAppliedToDamages = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountForfeitedUnused = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateForfeitedAdvances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateForfeitedAdvances_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateInspectionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssessedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LineOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateInspectionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateInspectionLines_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsReadByManagement = table.Column<bool>(type: "bit", nullable: false),
                    IsReadByTenant = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateMessages_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateSettlements_VacateRequests_VacateRequestId",
                        column: x => x.VacateRequestId,
                        principalTable: "VacateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintApprovalActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintApprovalActions_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: true),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintAttachments_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintLandlordDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByLandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintLandlordDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintLandlordDecisions_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsReadByManagement = table.Column<bool>(type: "bit", nullable: false),
                    IsReadByTenant = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintMessages_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedByTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedByAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintStatusHistory_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintWorkAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantVerdict = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantVerdictReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantVerdictAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintWorkAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintWorkAttempts_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    County = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Constituency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Ward = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GoogleMapsLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RentDueDay = table.Column<int>(type: "int", nullable: false),
                    BillableGracePeriodMonths = table.Column<int>(type: "int", nullable: false),
                    VacateNoticeDeadlineDay = table.Column<int>(type: "int", nullable: false),
                    SitDeposit = table.Column<bool>(type: "bit", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Flats_Landlords_LandlordId",
                        column: x => x.LandlordId,
                        principalTable: "Landlords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VacateAppealLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateAppealId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateInspectionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateAppealLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateAppealLines_VacateAppeals_VacateAppealId",
                        column: x => x.VacateAppealId,
                        principalTable: "VacateAppeals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VacateAppealLines_VacateInspectionLines_VacateInspectionLineId",
                        column: x => x.VacateInspectionLineId,
                        principalTable: "VacateInspectionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateInspectionLineAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateInspectionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateInspectionLineAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateInspectionLineAttachments_VacateInspectionLines_VacateInspectionLineId",
                        column: x => x.VacateInspectionLineId,
                        principalTable: "VacateInspectionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VacateCheckoutAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VacateSettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultDesc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacateCheckoutAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VacateCheckoutAttempts_VacateSettlements_VacateSettlementId",
                        column: x => x.VacateSettlementId,
                        principalTable: "VacateSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentFlats",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentFlats", x => new { x.AgentId, x.FlatId });
                    table.ForeignKey(
                        name: "FK_AgentFlats_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentFlats_Flats_FlatId",
                        column: x => x.FlatId,
                        principalTable: "Flats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlatEditRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedFlatName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedCounty = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedConstituency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedWard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedRentDueDay = table.Column<int>(type: "int", nullable: true),
                    ProposedBillableGracePeriodMonths = table.Column<int>(type: "int", nullable: true),
                    ProposedVacateNoticeDeadlineDay = table.Column<int>(type: "int", nullable: true),
                    ProposedSitDeposit = table.Column<bool>(type: "bit", nullable: true),
                    ProposedGoogleMapsLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClearAgent = table.Column<bool>(type: "bit", nullable: false),
                    SubmissionNotes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentApprovalStepOrder = table.Column<int>(type: "int", nullable: true),
                    ApprovalAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LandlordDecision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LandlordDecisionNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LandlordActionedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlatEditRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlatEditRequests_Flats_FlatId",
                        column: x => x.FlatId,
                        principalTable: "Flats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Houses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HouseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DepositFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OccupancyStatus = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "Vacant"),
                    PaymentStatus = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "NotPaid"),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsListingHidden = table.Column<bool>(type: "bit", nullable: false),
                    CommentsMuted = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Houses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Houses_Flats_FlatId",
                        column: x => x.FlatId,
                        principalTable: "Flats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Houses_HouseTypes_HouseTypeId",
                        column: x => x.HouseTypeId,
                        principalTable: "HouseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HouseImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseImages_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseListingBookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplorerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseListingBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseListingBookmarks_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseListingComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplorerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnonymousDeviceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommenterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseListingComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseListingComments_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseListingLikes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplorerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnonymousDeviceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsLike = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseListingLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseListingLikes_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseListingRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplorerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnonymousDeviceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Stars = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseListingRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseListingRatings_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListingViewingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExplorerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeclineReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReassignedFromAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReassignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FeedbackPromptSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgentRating = table.Column<int>(type: "int", nullable: true),
                    RescheduleReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RescheduleCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingViewingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingViewingSessions_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingRentChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewRentFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    NewDepositFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    EffectiveMonth = table.Column<int>(type: "int", nullable: false),
                    EffectiveYear = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingRentChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingRentChanges_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantHouseHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TenantPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HouseNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FlatName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenancyCycle = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantHouseHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantHouseHistories_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmergencyContactName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantStatus = table.Column<int>(type: "int", nullable: false),
                    HasCompletedInitialPayment = table.Column<bool>(type: "bit", nullable: false),
                    TenancyCycle = table.Column<int>(type: "int", nullable: false),
                    TemporaryInitialPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationEmailSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseStartMonth = table.Column<int>(type: "int", nullable: true),
                    LeaseStartYear = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenants_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tenants_PortalUsers_Id",
                        column: x => x.Id,
                        principalTable: "PortalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsReadByAgent = table.Column<bool>(type: "bit", nullable: false),
                    IsReadByExplorer = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionMessages_ListingViewingSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ListingViewingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DepositAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ServiceChargeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreditApplied = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RequestedDistributionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    PaymentType = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: true),
                    MpesaReceiptNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MpesaTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MerchantRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MpesaResultCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MpesaResultDesc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsInitialPayment = table.Column<bool>(type: "bit", nullable: false),
                    TenancyCycle = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Flats_FlatId",
                        column: x => x.FlatId,
                        principalTable: "Flats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Houses_HouseId",
                        column: x => x.HouseId,
                        principalTable: "Houses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MpesaReceiptNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AmountApplied = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentApplications_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCheckoutAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AttemptStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultDesc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCheckoutAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentCheckoutAttempts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admins_CreatedBy",
                table: "Admins",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Admins_Email",
                table: "Admins",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AgentFlats_FlatId",
                table: "AgentFlats",
                column: "FlatId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintApprovalActions_ComplaintId",
                table: "ComplaintApprovalActions",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintAttachments_ComplaintId",
                table: "ComplaintAttachments",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintLandlordDecisions_ComplaintId",
                table: "ComplaintLandlordDecisions",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintMessages_ComplaintId",
                table: "ComplaintMessages",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_ComplaintTypeId",
                table: "Complaints",
                column: "ComplaintTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintStatusHistory_ComplaintId",
                table: "ComplaintStatusHistory",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintWorkAttempts_ComplaintId",
                table: "ComplaintWorkAttempts",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_FlatEditRequests_FlatId",
                table: "FlatEditRequests",
                column: "FlatId");

            migrationBuilder.CreateIndex(
                name: "IX_Flats_FlatName",
                table: "Flats",
                column: "FlatName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flats_LandlordId",
                table: "Flats",
                column: "LandlordId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseImages_HouseId",
                table: "HouseImages",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseListingBookmarks_HouseId_ExplorerId",
                table: "HouseListingBookmarks",
                columns: new[] { "HouseId", "ExplorerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseListingComments_HouseId",
                table: "HouseListingComments",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseListingLikes_HouseId_ExplorerId",
                table: "HouseListingLikes",
                columns: new[] { "HouseId", "ExplorerId" },
                unique: true,
                filter: "[ExplorerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HouseListingRatings_HouseId_ExplorerId",
                table: "HouseListingRatings",
                columns: new[] { "HouseId", "ExplorerId" },
                unique: true,
                filter: "[ExplorerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Houses_FlatId",
                table: "Houses",
                column: "FlatId");

            migrationBuilder.CreateIndex(
                name: "IX_Houses_HouseTypeId",
                table: "Houses",
                column: "HouseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingViewingSessions_HouseId",
                table: "ListingViewingSessions",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_IsPortalUser",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "IsPortalUser" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Audience",
                table: "Notifications",
                column: "Audience");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsRead",
                table: "Notifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TargetUserId",
                table: "Notifications",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentApplications_PaymentId",
                table: "PaymentApplications",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCheckoutAttempts_PaymentId",
                table: "PaymentCheckoutAttempts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_FlatId",
                table: "Payments",
                column: "FlatId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_HouseId",
                table: "Payments",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRentChanges_HouseId",
                table: "PendingRentChanges",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PortalUsers_Email",
                table: "PortalUsers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMessages_SessionId",
                table: "SessionMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMessages_SessionId_AgentId",
                table: "SessionMessages",
                columns: new[] { "SessionId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantHouseHistories_HouseId",
                table: "TenantHouseHistories",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_HouseId",
                table: "Tenants",
                column: "HouseId");

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_PortalUserId_Role_Version",
                table: "TermsAcceptances",
                columns: new[] { "PortalUserId", "Role", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_TermsAndConditions_Role",
                table: "TermsAndConditions",
                column: "Role",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TermsHistories_Role_Version",
                table: "TermsHistories",
                columns: new[] { "Role", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_VacateAppealLines_VacateAppealId",
                table: "VacateAppealLines",
                column: "VacateAppealId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateAppealLines_VacateInspectionLineId",
                table: "VacateAppealLines",
                column: "VacateInspectionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateAppeals_VacateRequestId",
                table: "VacateAppeals",
                column: "VacateRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateApprovalActions_VacateRequestId",
                table: "VacateApprovalActions",
                column: "VacateRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateCheckoutAttempts_VacateSettlementId",
                table: "VacateCheckoutAttempts",
                column: "VacateSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateForfeitedAdvances_VacateRequestId",
                table: "VacateForfeitedAdvances",
                column: "VacateRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateInspectionLineAttachments_VacateInspectionLineId",
                table: "VacateInspectionLineAttachments",
                column: "VacateInspectionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateInspectionLines_VacateRequestId",
                table: "VacateInspectionLines",
                column: "VacateRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateMessages_VacateRequestId",
                table: "VacateMessages",
                column: "VacateRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VacateSettlements_VacateRequestId",
                table: "VacateSettlements",
                column: "VacateRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accountants");

            migrationBuilder.DropTable(
                name: "AdminUsers");

            migrationBuilder.DropTable(
                name: "AgentFlats");

            migrationBuilder.DropTable(
                name: "ApprovalSequenceSteps");

            migrationBuilder.DropTable(
                name: "ComplaintApprovalActions");

            migrationBuilder.DropTable(
                name: "ComplaintAttachments");

            migrationBuilder.DropTable(
                name: "ComplaintLandlordDecisions");

            migrationBuilder.DropTable(
                name: "ComplaintMessages");

            migrationBuilder.DropTable(
                name: "ComplaintStatusHistory");

            migrationBuilder.DropTable(
                name: "ComplaintWorkAttempts");

            migrationBuilder.DropTable(
                name: "Deductions");

            migrationBuilder.DropTable(
                name: "Explorers");

            migrationBuilder.DropTable(
                name: "FlatEditRequests");

            migrationBuilder.DropTable(
                name: "HouseImages");

            migrationBuilder.DropTable(
                name: "HouseListingBookmarks");

            migrationBuilder.DropTable(
                name: "HouseListingComments");

            migrationBuilder.DropTable(
                name: "HouseListingLikes");

            migrationBuilder.DropTable(
                name: "HouseListingRatings");

            migrationBuilder.DropTable(
                name: "Managers");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PaymentApplications");

            migrationBuilder.DropTable(
                name: "PaymentCheckoutAttempts");

            migrationBuilder.DropTable(
                name: "PendingRentChanges");

            migrationBuilder.DropTable(
                name: "Secretaries");

            migrationBuilder.DropTable(
                name: "ServiceChargeSettings");

            migrationBuilder.DropTable(
                name: "SessionMessages");

            migrationBuilder.DropTable(
                name: "SuperAdmins");

            migrationBuilder.DropTable(
                name: "TenantHouseHistories");

            migrationBuilder.DropTable(
                name: "TermsAcceptances");

            migrationBuilder.DropTable(
                name: "TermsAndConditions");

            migrationBuilder.DropTable(
                name: "TermsHistories");

            migrationBuilder.DropTable(
                name: "VacateAppealLines");

            migrationBuilder.DropTable(
                name: "VacateApprovalActions");

            migrationBuilder.DropTable(
                name: "VacateCheckoutAttempts");

            migrationBuilder.DropTable(
                name: "VacateForfeitedAdvances");

            migrationBuilder.DropTable(
                name: "VacateInspectionLineAttachments");

            migrationBuilder.DropTable(
                name: "VacateMessages");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "ListingViewingSessions");

            migrationBuilder.DropTable(
                name: "Admins");

            migrationBuilder.DropTable(
                name: "VacateAppeals");

            migrationBuilder.DropTable(
                name: "VacateSettlements");

            migrationBuilder.DropTable(
                name: "VacateInspectionLines");

            migrationBuilder.DropTable(
                name: "ComplaintTypes");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "VacateRequests");

            migrationBuilder.DropTable(
                name: "Houses");

            migrationBuilder.DropTable(
                name: "Flats");

            migrationBuilder.DropTable(
                name: "HouseTypes");

            migrationBuilder.DropTable(
                name: "Landlords");

            migrationBuilder.DropTable(
                name: "PortalUsers");
        }
    }
}
