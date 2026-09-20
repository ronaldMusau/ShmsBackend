using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlatHouseTypeNumberUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Houses_FlatId",
                table: "Houses");

            migrationBuilder.CreateIndex(
                name: "IX_Houses_FlatId_HouseTypeId_HouseNumber",
                table: "Houses",
                columns: new[] { "FlatId", "HouseTypeId", "HouseNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Houses_FlatId_HouseTypeId_HouseNumber",
                table: "Houses");

            migrationBuilder.CreateIndex(
                name: "IX_Houses_FlatId",
                table: "Houses",
                column: "FlatId");
        }
    }
}
