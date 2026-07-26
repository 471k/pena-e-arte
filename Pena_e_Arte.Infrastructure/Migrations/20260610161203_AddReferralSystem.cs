using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PendingReferralCodeId",
                table: "studios",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "referral_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSingleUse = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referral_codes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referral_redemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReferralCodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NewStudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RedeemedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DiscountApplied = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referral_redemptions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_studios_pending_referral_code_id",
                table: "studios",
                column: "PendingReferralCodeId");

            migrationBuilder.CreateIndex(
                name: "ix_referral_codes_code",
                table: "referral_codes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referral_codes_studio_id",
                table: "referral_codes",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "ix_referral_redemptions_code_id",
                table: "referral_redemptions",
                column: "ReferralCodeId");

            migrationBuilder.CreateIndex(
                name: "ix_referral_redemptions_new_studio_id",
                table: "referral_redemptions",
                column: "NewStudioId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "referral_codes");

            migrationBuilder.DropTable(
                name: "referral_redemptions");

            migrationBuilder.DropIndex(
                name: "ix_studios_pending_referral_code_id",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "PendingReferralCodeId",
                table: "studios");
        }
    }
}
