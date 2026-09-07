using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExistingTenantHouseFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAwaitingExistingTenant",
                table: "Houses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExistingTenant",
                table: "FlatEditHouseTypeChanges",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAwaitingExistingTenant",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "ExistingTenant",
                table: "FlatEditHouseTypeChanges");
        }
    }
}
