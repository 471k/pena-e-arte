using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "social_account_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubjectType = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Handle = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VerificationMethod = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EncryptedToken = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokenExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PendingVerificationCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PendingCodeExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_account_links", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_social_account_links_studio_id",
                table: "social_account_links",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "ix_social_account_links_subject_platform",
                table: "social_account_links",
                columns: new[] { "SubjectType", "SubjectId", "Platform" },
                unique: true);

            // Backfill existing free-text Studio.InstagramHandle values into an
            // *unverified* SocialAccountLink row, so the new table becomes the single
            // source of truth for the public API immediately (see database.md's
            // zero-downtime migration order — the column itself is not dropped here,
            // only the public response shape changes; see docs/claude/architecture.md's
            // Social Verification entry for the scheduled follow-up removal).
            migrationBuilder.Sql("""
                INSERT INTO social_account_links
                  (Id, StudioId, SubjectType, SubjectId, Platform, Handle, IsVerified, CreatedAt, UpdatedAt)
                SELECT
                  UUID(), Id, 'Studio', Id, 'Instagram', InstagramHandle, 0, NOW(6), NOW(6)
                FROM studios
                WHERE InstagramHandle IS NOT NULL AND InstagramHandle <> ''
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_account_links");
        }
    }
}
