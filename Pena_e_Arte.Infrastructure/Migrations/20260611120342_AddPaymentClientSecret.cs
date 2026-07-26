using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentClientSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientSecret",
                table: "payments",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_referral_codes_studios_StudioId",
                table: "referral_codes",
                column: "StudioId",
                principalTable: "studios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_referral_redemptions_referral_codes_ReferralCodeId",
                table: "referral_redemptions",
                column: "ReferralCodeId",
                principalTable: "referral_codes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_referral_codes_studios_StudioId",
                table: "referral_codes");

            migrationBuilder.DropForeignKey(
                name: "FK_referral_redemptions_referral_codes_ReferralCodeId",
                table: "referral_redemptions");

            migrationBuilder.DropColumn(
                name: "ClientSecret",
                table: "payments");
        }
    }
}
