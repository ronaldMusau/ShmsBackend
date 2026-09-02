using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementDocumentSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgreementTemplateHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgreementTemplateHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgreementTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgreementTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAgreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    UploadedFilePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerifiedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAgreements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserIdDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortalUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrontImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BackImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgreementTemplateHistories_Role_Version",
                table: "AgreementTemplateHistories",
                columns: new[] { "Role", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_AgreementTemplates_Role",
                table: "AgreementTemplates",
                column: "Role",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAgreements_PortalUserId",
                table: "UserAgreements",
                column: "PortalUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdDocuments_PortalUserId",
                table: "UserIdDocuments",
                column: "PortalUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgreementTemplateHistories");

            migrationBuilder.DropTable(
                name: "AgreementTemplates");

            migrationBuilder.DropTable(
                name: "UserAgreements");

            migrationBuilder.DropTable(
                name: "UserIdDocuments");
        }
    }
}
