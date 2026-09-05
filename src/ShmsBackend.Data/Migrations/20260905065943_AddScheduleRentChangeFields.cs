using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleRentChangeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProposedEffectiveMonth",
                table: "FlatEditHouseTypeChanges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedEffectiveYear",
                table: "FlatEditHouseTypeChanges",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedEffectiveMonth",
                table: "FlatEditHouseTypeChanges");

            migrationBuilder.DropColumn(
                name: "ProposedEffectiveYear",
                table: "FlatEditHouseTypeChanges");
        }
    }
}
