using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThreatGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedIps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedIps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecuritySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IpFailThreshold = table.Column<int>(type: "int", nullable: false),
                    IpDistinctAccountsThreshold = table.Column<int>(type: "int", nullable: false),
                    IpFailWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    SharedIpFactor = table.Column<int>(type: "int", nullable: false),
                    NotFoundThreshold = table.Column<int>(type: "int", nullable: false),
                    NotFoundWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    RateLimitedThreshold = table.Column<int>(type: "int", nullable: false),
                    RateLimitedWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    BlockMinutesBase = table.Column<int>(type: "int", nullable: false),
                    BlockMaxHours = table.Column<int>(type: "int", nullable: false),
                    EscalateAfterBlocks = table.Column<int>(type: "int", nullable: false),
                    BlockPrivateIps = table.Column<bool>(type: "bit", nullable: false),
                    SprayDistinctAccounts = table.Column<int>(type: "int", nullable: false),
                    DistinctIpsThreshold = table.Column<int>(type: "int", nullable: false),
                    DistinctIpsWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    ForbiddenThreshold = table.Column<int>(type: "int", nullable: false),
                    ForbiddenWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    ReadsPerMinuteThreshold = table.Column<int>(type: "int", nullable: false),
                    AccountStrikesBeforeDeactivate = table.Column<int>(type: "int", nullable: false),
                    AutoDeactivateEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockdownActive = table.Column<bool>(type: "bit", nullable: false),
                    LockdownReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockdownBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LockdownAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockedIps");

            migrationBuilder.DropTable(
                name: "LoginActivities");

            migrationBuilder.DropTable(
                name: "SecuritySettings");
        }
    }
}
