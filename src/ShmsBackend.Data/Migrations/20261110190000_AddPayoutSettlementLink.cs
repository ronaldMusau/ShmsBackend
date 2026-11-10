using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110190000_AddPayoutSettlementLink")]
    public partial class AddPayoutSettlementLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "SettlementId", table: "B2CPayouts", type: "uniqueidentifier", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_B2CPayouts_SettlementId", table: "B2CPayouts", column: "SettlementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_B2CPayouts_SettlementId", table: "B2CPayouts");
            migrationBuilder.DropColumn(name: "SettlementId", table: "B2CPayouts");
        }
    }
}
