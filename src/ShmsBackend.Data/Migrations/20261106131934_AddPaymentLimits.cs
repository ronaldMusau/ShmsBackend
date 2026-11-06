using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxDailyPaymentPerTenant",
                table: "CompanySettings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxSinglePaymentAmount",
                table: "CompanySettings",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxDailyPaymentPerTenant",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "MaxSinglePaymentAmount",
                table: "CompanySettings");
        }
    }
}
