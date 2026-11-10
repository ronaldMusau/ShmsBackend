using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110180000_AddMoneyApprovals")]
    public partial class AddMoneyApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "CurrentStepOrder", table: "B2CPayouts", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "SentByUserId", table: "B2CPayouts", type: "nvarchar(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "SentByName", table: "B2CPayouts", type: "nvarchar(150)", maxLength: 150, nullable: true);

            migrationBuilder.CreateTable(
                name: "ReversalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IssueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    House = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequestedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_ReversalRequests", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_ReversalRequests_IssueKey", table: "ReversalRequests", column: "IssueKey");
            migrationBuilder.CreateIndex(name: "IX_ReversalRequests_Status", table: "ReversalRequests", column: "Status");

            migrationBuilder.CreateTable(
                name: "MoneyApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApproverName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ActionedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_MoneyApprovalActions", x => x.Id); });
            migrationBuilder.CreateIndex(name: "IX_MoneyApprovalActions_Kind_SubjectId", table: "MoneyApprovalActions", columns: new[] { "Kind", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MoneyApprovalActions");
            migrationBuilder.DropTable(name: "ReversalRequests");
            migrationBuilder.DropColumn(name: "CurrentStepOrder", table: "B2CPayouts");
            migrationBuilder.DropColumn(name: "SentByUserId", table: "B2CPayouts");
            migrationBuilder.DropColumn(name: "SentByName", table: "B2CPayouts");
        }
    }
}
