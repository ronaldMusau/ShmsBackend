using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TeamSize",
                table: "Managers",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Admins_CreatedBy",
                table: "Admins",
                column: "CreatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Admins_Admins_CreatedBy",
                table: "Admins",
                column: "CreatedBy",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Admins_Admins_CreatedBy",
                table: "Admins");

            migrationBuilder.DropIndex(
                name: "IX_Admins_CreatedBy",
                table: "Admins");

            migrationBuilder.AlterColumn<int>(
                name: "TeamSize",
                table: "Managers",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
