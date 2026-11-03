using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceReadinessRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresConsentForm",
                table: "services",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDepositPaid",
                table: "services",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresIntakeForm",
                table: "services",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresReferenceImage",
                table: "services",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresConsentForm",
                table: "services");

            migrationBuilder.DropColumn(
                name: "RequiresDepositPaid",
                table: "services");

            migrationBuilder.DropColumn(
                name: "RequiresIntakeForm",
                table: "services");

            migrationBuilder.DropColumn(
                name: "RequiresReferenceImage",
                table: "services");
        }
    }
}
