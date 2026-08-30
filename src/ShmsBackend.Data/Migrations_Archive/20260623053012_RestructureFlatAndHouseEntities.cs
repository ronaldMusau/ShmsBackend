using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestructureFlatAndHouseEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Flats_Agents_AgentId",
                table: "Flats");

            migrationBuilder.DropForeignKey(
                name: "FK_Flats_Houses_HouseId",
                table: "Flats");

            migrationBuilder.DropForeignKey(
                name: "FK_Houses_Agents_AgentId",
                table: "Houses");

            migrationBuilder.DropForeignKey(
                name: "FK_Houses_Landlords_LandlordId",
                table: "Houses");

            migrationBuilder.DropIndex(
                name: "IX_Houses_AgentId",
                table: "Houses");

            migrationBuilder.DropIndex(
                name: "IX_Flats_AgentId",
                table: "Flats");

            migrationBuilder.DropIndex(
                name: "IX_Flats_HouseId",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Bathrooms",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Bedrooms",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Bathrooms",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Bedrooms",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "FloorNumber",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "HouseId",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "Flats");

            migrationBuilder.RenameColumn(
                name: "LandlordId",
                table: "Houses",
                newName: "FlatId");

            migrationBuilder.RenameIndex(
                name: "IX_Houses_LandlordId",
                table: "Houses",
                newName: "IX_Houses_FlatId");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "Flats",
                newName: "FlatName");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Houses",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Houses",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<decimal>(
                name: "DepositFee",
                table: "Houses",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "HouseNumber",
                table: "Houses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HouseType",
                table: "Houses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OccupancyStatus",
                table: "Houses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Vacant");

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Houses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "NotPaid");

            migrationBuilder.AddColumn<decimal>(
                name: "RentFee",
                table: "Houses",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Ward",
                table: "Flats",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Flats",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Flats",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<string>(
                name: "County",
                table: "Flats",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Constituency",
                table: "Flats",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flats_FlatName",
                table: "Flats",
                column: "FlatName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Houses_Flats_FlatId",
                table: "Houses",
                column: "FlatId",
                principalTable: "Flats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Houses_Flats_FlatId",
                table: "Houses");

            migrationBuilder.DropIndex(
                name: "IX_Flats_FlatName",
                table: "Flats");

            migrationBuilder.DropColumn(
                name: "DepositFee",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "HouseNumber",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "HouseType",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "OccupancyStatus",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Houses");

            migrationBuilder.DropColumn(
                name: "RentFee",
                table: "Houses");

            migrationBuilder.RenameColumn(
                name: "FlatId",
                table: "Houses",
                newName: "LandlordId");

            migrationBuilder.RenameIndex(
                name: "IX_Houses_FlatId",
                table: "Houses",
                newName: "IX_Houses_LandlordId");

            migrationBuilder.RenameColumn(
                name: "FlatName",
                table: "Flats",
                newName: "Title");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Houses",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Houses",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Houses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "Houses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bathrooms",
                table: "Houses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Bedrooms",
                table: "Houses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Houses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Houses",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Houses",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Houses",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Houses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Houses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "Houses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Ward",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "Flats",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "Flats",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "County",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Constituency",
                table: "Flats",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Flats",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "Flats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bathrooms",
                table: "Flats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Bedrooms",
                table: "Flats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Flats",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Flats",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FloorNumber",
                table: "Flats",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "HouseId",
                table: "Flats",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Flats",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Flats",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Flats",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "Flats",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Houses_AgentId",
                table: "Houses",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Flats_AgentId",
                table: "Flats",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Flats_HouseId",
                table: "Flats",
                column: "HouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Flats_Agents_AgentId",
                table: "Flats",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Flats_Houses_HouseId",
                table: "Flats",
                column: "HouseId",
                principalTable: "Houses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Houses_Agents_AgentId",
                table: "Houses",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Houses_Landlords_LandlordId",
                table: "Houses",
                column: "LandlordId",
                principalTable: "Landlords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
