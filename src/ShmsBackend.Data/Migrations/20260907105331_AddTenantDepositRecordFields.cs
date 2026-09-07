using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDepositRecordFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DepositAlreadySitting",
                table: "Tenants",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExternalDepositAmount",
                table: "Tenants",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositAlreadySitting",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExternalDepositAmount",
                table: "Tenants");
        }
    }
}
