using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110170000_AddB2CPayouts")]
    public partial class AddB2CPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "B2CPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiverPhone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequestedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ApprovedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConversationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResultCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ResultDesc = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MpesaReceipt = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => { table.PrimaryKey("PK_B2CPayouts", x => x.Id); });

            migrationBuilder.CreateIndex(name: "IX_B2CPayouts_Status", table: "B2CPayouts", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_B2CPayouts_RequestedAt", table: "B2CPayouts", column: "RequestedAt");
            migrationBuilder.CreateIndex(name: "IX_B2CPayouts_ConversationId", table: "B2CPayouts", column: "ConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "B2CPayouts");
        }
    }
}
