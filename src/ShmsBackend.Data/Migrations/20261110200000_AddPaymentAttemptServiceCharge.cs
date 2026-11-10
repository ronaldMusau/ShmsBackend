using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShmsBackend.Data.Context;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ShmsDbContext))]
    [Migration("20261110200000_AddPaymentAttemptServiceCharge")]
    public partial class AddPaymentAttemptServiceCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "ServiceChargeAmount", table: "PaymentCheckoutAttempts", type: "decimal(18,2)", precision: 18, scale: 2, nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ServiceChargeAmount", table: "PaymentCheckoutAttempts");
        }
    }
}
