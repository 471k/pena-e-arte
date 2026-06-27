using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioImageEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create PortfolioImages table first so data migration can reference it.
            migrationBuilder.CreateTable(
                name: "PortfolioImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ImageUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_PortfolioImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioImages_artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // 2. Migrate existing JSON portfolio data to the new table.
            migrationBuilder.Sql(@"
                INSERT INTO PortfolioImages (Id, StudioId, ArtistId, ImageUrl, CreatedAt, UpdatedAt)
                SELECT
                    UUID(),
                    a.StudioId,
                    a.Id,
                    img.value,
                    UTC_TIMESTAMP(),
                    UTC_TIMESTAMP()
                FROM artists a
                CROSS JOIN JSON_TABLE(
                    COALESCE(a.PortfolioImages, '[]'),
                    '$[*]' COLUMNS (value VARCHAR(2048) PATH '$')
                ) AS img
                WHERE a.PortfolioImages IS NOT NULL
                  AND JSON_LENGTH(a.PortfolioImages) > 0;
            ");

            // 3. Drop the now-redundant JSON column (data is safe in PortfolioImages table).
            migrationBuilder.DropColumn(
                name: "PortfolioImages",
                table: "artists");

            migrationBuilder.AddColumn<Guid>(
                name: "PortfolioImageId",
                table: "Reviews",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AuthorUserId_PortfolioImageId",
                table: "Reviews",
                columns: new[] { "AuthorUserId", "PortfolioImageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_PortfolioImageId",
                table: "Reviews",
                column: "PortfolioImageId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioImages_ArtistId",
                table: "PortfolioImages",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "ix_PortfolioImages_studio_id",
                table: "PortfolioImages",
                column: "StudioId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_PortfolioImages_PortfolioImageId",
                table: "Reviews",
                column: "PortfolioImageId",
                principalTable: "PortfolioImages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_PortfolioImages_PortfolioImageId",
                table: "Reviews");

            migrationBuilder.DropTable(
                name: "PortfolioImages");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_AuthorUserId_PortfolioImageId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_PortfolioImageId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "PortfolioImageId",
                table: "Reviews");

            migrationBuilder.AddColumn<string>(
                name: "PortfolioImages",
                table: "artists",
                type: "json",
                nullable: false,
                defaultValue: "[]")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(@"
                UPDATE artists a
                SET a.PortfolioImages = (
                    SELECT COALESCE(JSON_ARRAYAGG(pi.ImageUrl), '[]')
                    FROM PortfolioImages pi
                    WHERE pi.ArtistId = a.Id AND pi.DeletedAt IS NULL
                );
            ");
        }
    }
}
