using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDesignShareToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "design_share_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Token = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DesignRevisionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRevoked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_share_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_design_share_tokens_design_revisions_DesignRevisionId",
                        column: x => x.DesignRevisionId,
                        principalTable: "design_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_design_share_tokens_DesignRevisionId",
                table: "design_share_tokens",
                column: "DesignRevisionId");

            migrationBuilder.CreateIndex(
                name: "ix_design_share_tokens_expires_at",
                table: "design_share_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_design_share_tokens_studio_id",
                table: "design_share_tokens",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "ix_design_share_tokens_token",
                table: "design_share_tokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "design_share_tokens");
        }
    }
}
