using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110110000_AddAuthenticatorDeadline")]
    public partial class AddAuthenticatorDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthenticatorDeadlineDays",
                table: "SecuritySettings",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<DateTime>(
                name: "AuthenticatorGraceFrom",
                table: "Admins",
                type: "datetime2",
                nullable: true);

            // Everyone who has not set up the app yet gets a full fresh window from the day this is applied,
            // so nobody is deactivated the moment the deadline goes live.
            migrationBuilder.Sql("UPDATE [Admins] SET [AuthenticatorGraceFrom] = SYSUTCDATETIME() WHERE [TotpEnabledAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthenticatorDeadlineDays", table: "SecuritySettings");
            migrationBuilder.DropColumn(name: "AuthenticatorGraceFrom", table: "Admins");
        }
    }
}
