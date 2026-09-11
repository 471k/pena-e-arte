using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StructureArtistAndInviteSpecializations : Migration
    {
        // Canonical Pena_e_Arte.Domain.Constants.TattooStyle values. Best-effort backfill only:
        // each old freeform Specializations value is matched against these REGEXP patterns (case
        // insensitive, allowing for spacing/hyphen variants); anything that doesn't match one of
        // them — e.g. "Portraits", "Lettering", "Minimalist", or "Black & Grey" as a shading
        // descriptor rather than the "Blackwork" linework style — is dropped. Specializations is
        // an informational artist-profile field, not billing/compliance data, and there is no
        // reliable way to map arbitrary freeform text onto the fixed style vocabulary.
        //
        // REGEXP (not LIKE) so "traditional" can use a negative lookbehind to exclude the
        // "traditional" inside "neo-traditional" — MySQL's ICU-backed regex engine (8.0.4+)
        // supports lookbehind. A plain LIKE '%neo%traditional%' exclusion would instead reject
        // the whole row whenever both "Traditional" and "Neo-Traditional" appear as separate
        // comma-separated values in the same old string, losing the standalone "traditional" tag.
        private static readonly (string Style, string Pattern)[] StyleMatches =
        [
            ("traditional",     "(?<!neo-)(?<!neo )traditional"),
            ("neo-traditional", "neo[- ]?traditional"),
            ("realism",         "realis"),
            ("blackwork",       "blackwork"),
            ("geometric",       "geometric"),
            ("watercolor",      "watercolou?r"),
            ("fineline",        "fine[- ]?line"),
            ("japanese",        "japanese"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ConvertToJson(migrationBuilder, "artists");
            ConvertToJson(migrationBuilder, "studio_join_invites");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConvertToText(migrationBuilder, "artists");
            ConvertToText(migrationBuilder, "studio_join_invites");
        }

        private static void ConvertToJson(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<string>(
                name: "specializations_json",
                table: table,
                type: "json",
                nullable: true);

            migrationBuilder.Sql($"UPDATE {table} SET specializations_json = JSON_ARRAY();");

            foreach ((string style, string pattern) in StyleMatches)
            {
                migrationBuilder.Sql($"""
                    UPDATE {table}
                    SET specializations_json = JSON_ARRAY_APPEND(specializations_json, '$', '{style}')
                    WHERE Specializations IS NOT NULL
                      AND LOWER(Specializations) REGEXP '{pattern}';
                    """);
            }

            migrationBuilder.DropColumn(name: "Specializations", table: table);
            migrationBuilder.RenameColumn(name: "specializations_json", table: table, newName: "Specializations");
        }

        private static void ConvertToText(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<string>(
                    name: "specializations_text",
                    table: table,
                    type: "varchar(1000)",
                    maxLength: 1000,
                    nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql($"""
                UPDATE {table} t
                JOIN (
                    SELECT j.Id AS row_id, GROUP_CONCAT(style.value ORDER BY style.idx SEPARATOR ',') AS joined
                    FROM {table} j
                    JOIN JSON_TABLE(j.Specializations, '$[*]' COLUMNS (idx FOR ORDINALITY, value VARCHAR(50) PATH '$')) style
                    GROUP BY j.Id
                ) sub ON t.Id = sub.row_id
                SET t.specializations_text = sub.joined;
                """);

            migrationBuilder.DropColumn(name: "Specializations", table: table);
            migrationBuilder.RenameColumn(name: "specializations_text", table: table, newName: "Specializations");
        }
    }
}
