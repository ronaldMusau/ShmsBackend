using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PointsBalance",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RedemptionReference",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardsEmailEnabled",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardsInAppEnabled",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardsPushEnabled",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardEnabled",
                table: "Flats",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RewardSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsGlobalEnabled = table.Column<bool>(type: "bit", nullable: false),
                    InitialPaymentEarnRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RegularPaymentEarnRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RedemptionRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    AmountPaidOrRedeemed = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    RelatedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RedemptionReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardTransactions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactions_TenantId",
                table: "RewardTransactions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardSettings");

            migrationBuilder.DropTable(
                name: "RewardTransactions");

            migrationBuilder.DropColumn(
                name: "PointsBalance",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RedemptionReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RewardsEmailEnabled",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "RewardsInAppEnabled",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "RewardsPushEnabled",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "RewardEnabled",
                table: "Flats");
        }
    }
}
