using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlatEditHouseTypeChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlatEditHouseTypeChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatEditRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HouseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedPrefix = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedRentFee = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ProposedDepositFee = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ProposedCount = table.Column<int>(type: "int", nullable: true),
                    AdditionalCount = table.Column<int>(type: "int", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlatEditHouseTypeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlatEditHouseTypeChanges_FlatEditRequests_FlatEditRequestId",
                        column: x => x.FlatEditRequestId,
                        principalTable: "FlatEditRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlatEditHouseTypeChanges_FlatEditRequestId",
                table: "FlatEditHouseTypeChanges",
                column: "FlatEditRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlatEditHouseTypeChanges");
        }
    }
}
