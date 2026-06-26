using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDateOfBirthNationalIdAndDeactivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Landlords");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "PortalUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "PortalUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Admins",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Admins",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Admins");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfBirth",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Landlords",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
