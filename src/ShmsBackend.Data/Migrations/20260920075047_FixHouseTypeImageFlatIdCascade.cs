using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixHouseTypeImageFlatIdCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HouseTypeImages_Flats_FlatId",
                table: "HouseTypeImages");

            migrationBuilder.AddForeignKey(
                name: "FK_HouseTypeImages_Flats_FlatId",
                table: "HouseTypeImages",
                column: "FlatId",
                principalTable: "Flats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HouseTypeImages_Flats_FlatId",
                table: "HouseTypeImages");

            migrationBuilder.AddForeignKey(
                name: "FK_HouseTypeImages_Flats_FlatId",
                table: "HouseTypeImages",
                column: "FlatId",
                principalTable: "Flats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
