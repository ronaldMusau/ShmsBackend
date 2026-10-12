using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVacateAppealResolution : Migration
    {
        // Scaffolding also emitted HouseListingLikes index/column operations; those were removed because the
        // database already has UQ_HouseListingLike_Explorer / UQ_HouseListingLike_Device and AnonymousDeviceId
        // as nvarchar(200) (synced into the model without a migration in 540ccb5).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "VacateAppeals",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "VacateAppeals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedByAdminId",
                table: "VacateAppeals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Response",
                table: "VacateAppeals",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "VacateAppeals");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "VacateAppeals");

            migrationBuilder.DropColumn(
                name: "ResolvedByAdminId",
                table: "VacateAppeals");

            migrationBuilder.DropColumn(
                name: "Response",
                table: "VacateAppeals");
        }
    }
}
