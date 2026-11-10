using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110150000_AddReconciliationReviews")]
    public partial class AddReconciliationReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IssueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    House = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CheckoutRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MpesaAnswer = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReviewedByRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => { table.PrimaryKey("PK_ReconciliationReviews", x => x.Id); });

            migrationBuilder.CreateIndex(name: "IX_ReconciliationReviews_IssueKey", table: "ReconciliationReviews", column: "IssueKey", unique: true);
            migrationBuilder.CreateIndex(name: "IX_ReconciliationReviews_ReviewedAt", table: "ReconciliationReviews", column: "ReviewedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReconciliationReviews");
        }
    }
}
