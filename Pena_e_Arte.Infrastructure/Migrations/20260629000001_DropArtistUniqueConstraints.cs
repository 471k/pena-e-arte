using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pena_e_Arte.Infrastructure.Persistence;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <summary>
    /// Makes artists' (StudioId, Email) and Slug indexes non-unique, so a soft-deleted artist row
    /// (DeletedAt set, row still present) doesn't block re-adding an artist with the same email/slug.
    /// The app-level check (AnyAsync through global query filters) enforces uniqueness on active rows.
    ///
    /// History (2026-10-22): this file was hand-written without a .Designer.cs, so it had no
    /// [Migration]/[DbContext] attributes and EF never discovered it — it never ran anywhere, and
    /// GetPendingMigrations() reported nothing pending. Production and staging were fixed by hand on
    /// 2026-09-17 (same DDL + the history row), so this now only matters for databases built from
    /// scratch (local, CI, a disaster-recovery rebuild), which still got both indexes UNIQUE. Its old
    /// `DROP INDEX IF EXISTS` was not valid MySQL either. Up() is now idempotent: each index is
    /// changed only if it is still unique, and created if missing, so it is harmless wherever it
    /// already applied. A temporary procedure is used because MySqlConnector rejects @user variables
    /// unless AllowUserVariables is set.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260629000001_DropArtistUniqueConstraints")]
    public partial class DropArtistUniqueConstraints : Migration
    {
        private const string ProcedureName = "tattooos_tmp_artist_indexes";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            EnsureIndex(migrationBuilder, unique: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            EnsureIndex(migrationBuilder, unique: true);
        }

        private static void EnsureIndex(MigrationBuilder migrationBuilder, bool unique)
        {
            // NON_UNIQUE of an index that has the wrong uniqueness: 1 when we want it unique, 0 when not.
            int wrong = unique ? 1 : 0;
            string kind = unique ? "UNIQUE INDEX" : "INDEX";

            migrationBuilder.Sql($"DROP PROCEDURE IF EXISTS {ProcedureName};");
            migrationBuilder.Sql($"""
                CREATE PROCEDURE {ProcedureName}()
                BEGIN
                    DECLARE present INT;
                    DECLARE wrongKind INT;

                    SELECT COUNT(*), COALESCE(SUM(NON_UNIQUE = {wrong}), 0) INTO present, wrongKind
                    FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'artists' AND INDEX_NAME = 'ix_artists_studio_email';
                    IF present = 0 THEN
                        ALTER TABLE artists ADD {kind} ix_artists_studio_email (StudioId, Email);
                    ELSEIF wrongKind > 0 THEN
                        ALTER TABLE artists DROP INDEX ix_artists_studio_email, ADD {kind} ix_artists_studio_email (StudioId, Email);
                    END IF;

                    SELECT COUNT(*), COALESCE(SUM(NON_UNIQUE = {wrong}), 0) INTO present, wrongKind
                    FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'artists' AND INDEX_NAME = 'ix_artists_slug';
                    IF present = 0 THEN
                        ALTER TABLE artists ADD {kind} ix_artists_slug (Slug);
                    ELSEIF wrongKind > 0 THEN
                        ALTER TABLE artists DROP INDEX ix_artists_slug, ADD {kind} ix_artists_slug (Slug);
                    END IF;
                END;
                """);
            migrationBuilder.Sql($"CALL {ProcedureName}();");
            migrationBuilder.Sql($"DROP PROCEDURE {ProcedureName};");
        }
    }
}
