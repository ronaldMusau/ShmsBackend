using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancyCycleToRewardTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenancyCycle",
                table: "RewardTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows: where RelatedPaymentId resolves to a real Payment row, use that
            // payment's TenancyCycle (authoritative for Earned rows, and for Redeemed rows that happened
            // to link to a real payment). Where RelatedPaymentId is null or doesn't resolve (the
            // ambiguous Redeemed-row case), fall back to the tenant's CURRENT TenancyCycle — a
            // null-linked redemption is far more likely to be a real, recent, current-cycle transaction
            // than a truly orphaned historical one, and this avoids permanently hiding what might be
            // legitimate current-cycle history.
            migrationBuilder.Sql(@"
                UPDATE rt
                SET rt.TenancyCycle = COALESCE(p.TenancyCycle, t.TenancyCycle)
                FROM RewardTransactions rt
                LEFT JOIN Payments p ON p.Id = rt.RelatedPaymentId
                INNER JOIN Tenants t ON t.Id = rt.TenantId;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenancyCycle",
                table: "RewardTransactions");
        }
    }
}
