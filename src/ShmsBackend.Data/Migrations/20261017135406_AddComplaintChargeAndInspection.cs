using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintChargeAndInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TenantChargeDecidedAt",
                table: "Complaints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantChargeDecision",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantChargeDeclineReason",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TenantChargePaidAt",
                table: "Complaints",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantChargePaidBy",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantChargePaidByAdminId",
                table: "Complaints",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantChargeReceipt",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComplaintCheckoutAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaidBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InitiatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttemptStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultDesc = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MpesaReceiptNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintCheckoutAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintCheckoutAttempts_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoundNumber = table.Column<int>(type: "int", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintInspections_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCheckoutAttempts_CheckoutRequestId",
                table: "ComplaintCheckoutAttempts",
                column: "CheckoutRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCheckoutAttempts_ComplaintId",
                table: "ComplaintCheckoutAttempts",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintInspections_AgentId",
                table: "ComplaintInspections",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintInspections_ComplaintId_RoundNumber",
                table: "ComplaintInspections",
                columns: new[] { "ComplaintId", "RoundNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplaintCheckoutAttempts");

            migrationBuilder.DropTable(
                name: "ComplaintInspections");

            migrationBuilder.DropColumn(
                name: "TenantChargeDecidedAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargeDecision",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargeDeclineReason",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargePaidAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargePaidBy",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargePaidByAdminId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantChargeReceipt",
                table: "Complaints");
        }
    }
}
