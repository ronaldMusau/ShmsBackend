using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShmsBackend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseTypeImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseTypeImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlatId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseTypeImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseTypeImages_Flats_FlatId",
                        column: x => x.FlatId,
                        principalTable: "Flats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HouseTypeImages_HouseTypes_HouseTypeId",
                        column: x => x.HouseTypeId,
                        principalTable: "HouseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseTypeImages_FlatId",
                table: "HouseTypeImages",
                column: "FlatId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseTypeImages_HouseTypeId",
                table: "HouseTypeImages",
                column: "HouseTypeId");

            // Data backfill: HouseController.UploadImages saves the same physical files once per
            // request and links them to every house in the target house-type group, so existing
            // HouseImage rows for houses of the same (FlatId, HouseTypeId) genuinely share identical
            // ImagePath strings for houses photographed together in one batch. This collapses those
            // per-house duplicates into one row per distinct path per group, oldest upload first, capped
            // at 5 (matching the existing per-house cap). HouseImages itself is left fully intact —
            // additive only. If any group has more than 5 distinct paths (different houses of the same
            // type genuinely had non-overlapping images), only the first 5 by upload date are migrated
            // here and a RAISERROR is emitted identifying the group and every distinct path found, so
            // it can be reviewed by a human — this migration does not silently drop data without saying so.
            migrationBuilder.Sql(@"
                IF OBJECT_ID('tempdb..#DistinctPaths') IS NOT NULL DROP TABLE #DistinctPaths;

                SELECT
                    h.FlatId,
                    h.HouseTypeId,
                    hi.ImagePath,
                    MIN(hi.CreatedAt) AS FirstCreatedAt
                INTO #DistinctPaths
                FROM HouseImages hi
                INNER JOIN Houses h ON h.Id = hi.HouseId
                GROUP BY h.FlatId, h.HouseTypeId, hi.ImagePath;

                INSERT INTO HouseTypeImages (Id, FlatId, HouseTypeId, ImagePath, SortOrder, CreatedAt)
                SELECT NEWID(), FlatId, HouseTypeId, ImagePath, rn - 1, FirstCreatedAt
                FROM (
                    SELECT *,
                           ROW_NUMBER() OVER (PARTITION BY FlatId, HouseTypeId ORDER BY FirstCreatedAt ASC) AS rn
                    FROM #DistinctPaths
                ) ranked
                WHERE rn <= 5;

                DECLARE @Warnings TABLE (RowId INT IDENTITY(1,1), Msg NVARCHAR(MAX));
                INSERT INTO @Warnings (Msg)
                SELECT 'HouseTypeImages review needed: Flat ' + CAST(FlatId AS NVARCHAR(36))
                     + ' / HouseType ' + CAST(HouseTypeId AS NVARCHAR(36))
                     + ' has ' + CAST(COUNT(*) AS NVARCHAR(10))
                     + ' distinct image paths across its houses (only the first 5 by earliest upload were migrated): '
                     + STRING_AGG(CAST(ImagePath AS NVARCHAR(MAX)), ' | ')
                FROM #DistinctPaths
                GROUP BY FlatId, HouseTypeId
                HAVING COUNT(*) > 5;

                DECLARE @i INT = 1, @n INT = (SELECT COUNT(*) FROM @Warnings), @m NVARCHAR(MAX);
                WHILE @i <= @n
                BEGIN
                    SELECT @m = Msg FROM @Warnings WHERE RowId = @i;
                    RAISERROR('%s', 10, 1, @m) WITH NOWAIT;
                    SET @i += 1;
                END

                DROP TABLE #DistinctPaths;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseTypeImages");
        }
    }
}
