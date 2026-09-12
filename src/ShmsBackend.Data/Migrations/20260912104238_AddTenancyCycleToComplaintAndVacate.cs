using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancyCycleToComplaintAndVacate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TenancyCycle",
                table: "VacateRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenancyCycle",
                table: "Complaints",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill existing rows: match each row's CreatedAt against the TenantHouseHistories
            // date range (AssignedAt..RemovedAt, RemovedAt null = still current) it falls within, to
            // recover the accurate historical TenancyCycle. Where no range cleanly contains CreatedAt
            // (e.g. no history data for that tenant), fall back to the tenant's CURRENT TenancyCycle —
            // same safest-assumption principle as the RewardTransaction backfill.
            migrationBuilder.Sql(@"
                UPDATE v
                SET v.TenancyCycle = COALESCE(h.TenancyCycle, t.TenancyCycle)
                FROM VacateRequests v
                INNER JOIN Tenants t ON t.Id = v.TenantId
                OUTER APPLY (
                    SELECT TOP 1 h.TenancyCycle
                    FROM TenantHouseHistories h
                    WHERE h.TenantId = v.TenantId
                      AND v.CreatedAt >= h.AssignedAt
                      AND (h.RemovedAt IS NULL OR v.CreatedAt < h.RemovedAt)
                    ORDER BY h.AssignedAt DESC
                ) h;
            ");

            migrationBuilder.Sql(@"
                UPDATE c
                SET c.TenancyCycle = COALESCE(h.TenancyCycle, t.TenancyCycle)
                FROM Complaints c
                INNER JOIN Tenants t ON t.Id = c.TenantId
                OUTER APPLY (
                    SELECT TOP 1 h.TenancyCycle
                    FROM TenantHouseHistories h
                    WHERE h.TenantId = c.TenantId
                      AND c.CreatedAt >= h.AssignedAt
                      AND (h.RemovedAt IS NULL OR c.CreatedAt < h.RemovedAt)
                    ORDER BY h.AssignedAt DESC
                ) h;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenancyCycle",
                table: "VacateRequests");

            migrationBuilder.DropColumn(
                name: "TenancyCycle",
                table: "Complaints");
        }
    }
}
