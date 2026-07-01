using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropArtistUniqueConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop unique constraint on (StudioId, Email) — soft-deleted rows must not
            // block re-adding an artist with the same email. App-level check (AnyAsync
            // through global query filters) already enforces uniqueness on active rows.
            migrationBuilder.DropIndex(
                name: "ix_artists_studio_email",
                table: "artists");

            migrationBuilder.CreateIndex(
                name: "ix_artists_studio_email",
                table: "artists",
                columns: new[] { "StudioId", "Email" });

            // Slug unique index may not exist in all environments; drop with raw SQL.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_artists_slug ON artists;");

            migrationBuilder.CreateIndex(
                name: "ix_artists_slug",
                table: "artists",
                column: "Slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_artists_studio_email",
                table: "artists");

            migrationBuilder.CreateIndex(
                name: "ix_artists_studio_email",
                table: "artists",
                columns: new[] { "StudioId", "Email" },
                unique: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_artists_slug ON artists;");

            migrationBuilder.CreateIndex(
                name: "ix_artists_slug",
                table: "artists",
                column: "Slug",
                unique: true);
        }
    }
}
