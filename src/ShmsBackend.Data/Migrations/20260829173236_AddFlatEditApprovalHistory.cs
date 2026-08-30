using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlatEditApprovalHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlatEditApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatEditRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    ApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlatEditApprovalActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlatEditApprovalActions_FlatEditRequests_FlatEditRequestId",
                        column: x => x.FlatEditRequestId,
                        principalTable: "FlatEditRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlatEditLandlordDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatEditRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalAttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByLandlordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlatEditLandlordDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlatEditLandlordDecisions_FlatEditRequests_FlatEditRequestId",
                        column: x => x.FlatEditRequestId,
                        principalTable: "FlatEditRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlatEditApprovalActions_FlatEditRequestId",
                table: "FlatEditApprovalActions",
                column: "FlatEditRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FlatEditLandlordDecisions_FlatEditRequestId",
                table: "FlatEditLandlordDecisions",
                column: "FlatEditRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlatEditApprovalActions");

            migrationBuilder.DropTable(
                name: "FlatEditLandlordDecisions");
        }
    }
}
