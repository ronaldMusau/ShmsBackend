using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingFlatEditRequestColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProposedVacateNoticeDeadlineDay",
                table: "FlatEditRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProposedSitDeposit",
                table: "FlatEditRequests",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedVacateNoticeDeadlineDay",
                table: "FlatEditRequests");

            migrationBuilder.DropColumn(
                name: "ProposedSitDeposit",
                table: "FlatEditRequests");
        }
    }
}
