using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationFieldsToFlatAndPortalUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Constituency",
                table: "PortalUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "County",
                table: "PortalUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ward",
                table: "PortalUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Constituency",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "County",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ward",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Constituency",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "County",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "Ward",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "Constituency",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "County",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Ward",
                table: "Flats");
        }
    }
}
