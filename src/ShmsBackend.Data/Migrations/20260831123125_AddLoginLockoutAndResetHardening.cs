using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginLockoutAndResetHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "PortalUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsLockedOut",
                table: "PortalUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PasswordResetAttempts",
                table: "PortalUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Admins",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsLockedOut",
                table: "Admins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PasswordResetAttempts",
                table: "Admins",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "IsLockedOut",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "PasswordResetAttempts",
                table: "PortalUsers");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "IsLockedOut",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "PasswordResetAttempts",
                table: "Admins");
        }
    }
}
