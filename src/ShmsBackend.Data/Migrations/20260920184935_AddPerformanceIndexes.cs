using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ListingViewingSessions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "OccupancyStatus",
                table: "Houses",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "Vacant",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValue: "Vacant");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Complaints",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactions_TransactionType",
                table: "RewardTransactions",
                column: "TransactionType");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_IsDeleted_PaymentStatus",
                table: "Payments",
                columns: new[] { "IsDeleted", "PaymentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_Month_Year",
                table: "Payments",
                columns: new[] { "TenantId", "Month", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingViewingSessions_Status_AgentId_ScheduledAt",
                table: "ListingViewingSessions",
                columns: new[] { "Status", "AgentId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Houses_OccupancyStatus_IsListingHidden",
                table: "Houses",
                columns: new[] { "OccupancyStatus", "IsListingHidden" });

            migrationBuilder.CreateIndex(
                name: "IX_Flats_County_Constituency_Ward",
                table: "Flats",
                columns: new[] { "County", "Constituency", "Ward" });

            // IX_Complaints_Status already exists on the live database (created out-of-band, never
            // recorded in __EFMigrationsHistory) — omitted here to avoid a duplicate-index error.
            // The model config (HasIndex on Complaint.Status) stays so the snapshot reflects reality.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RewardTransactions_TransactionType",
                table: "RewardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_IsDeleted_PaymentStatus",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_Month_Year",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_ListingViewingSessions_Status_AgentId_ScheduledAt",
                table: "ListingViewingSessions");

            migrationBuilder.DropIndex(
                name: "IX_Houses_OccupancyStatus_IsListingHidden",
                table: "Houses");

            migrationBuilder.DropIndex(
                name: "IX_Flats_County_Constituency_Ward",
                table: "Flats");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ListingViewingSessions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "OccupancyStatus",
                table: "Houses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Vacant",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldDefaultValue: "Vacant");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Complaints",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");
        }
    }
}
