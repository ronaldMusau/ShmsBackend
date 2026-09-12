using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSoftDeletedTenantPointsBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill: TenantService.DeleteAsync did not reset PointsBalance when
            // soft-deleting a tenant, so a re-registration under the same email (which revives the
            // soft-deleted row rather than creating a new one) carried the old balance forward into
            // the new tenancy cycle. DeleteAsync now zeroes PointsBalance going forward; this backfills
            // every already-soft-deleted tenant so no stale balance is still sitting on a dormant row.
            migrationBuilder.Sql(@"
                UPDATE t
                SET t.PointsBalance = 0
                FROM Tenants t
                INNER JOIN PortalUsers u ON u.Id = t.Id
                WHERE u.IsDeleted = 1 AND t.PointsBalance <> 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible — the original (stale) balances were not recorded anywhere before this
            // backfill ran, so there is nothing to restore them from.
        }
    }
}
