using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantWarningAndVacateReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiationType",
                table: "VacateRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "VacateRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantWarnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarningNumber = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArrearsAtTime = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OverdueDaysAtTime = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWarnings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantWarnings_TenantId",
                table: "TenantWarnings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantWarnings");

            migrationBuilder.DropColumn(
                name: "InitiationType",
                table: "VacateRequests");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "VacateRequests");
        }
    }
}
