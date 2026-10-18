using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceChargeToComplaintVacateAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MpesaReceiptNumber",
                table: "VacateCheckoutAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                table: "VacateCheckoutAttempts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                table: "ComplaintCheckoutAttempts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MpesaReceiptNumber",
                table: "VacateCheckoutAttempts");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                table: "VacateCheckoutAttempts");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                table: "ComplaintCheckoutAttempts");
        }
    }
}
