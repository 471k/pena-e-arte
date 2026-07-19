using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    // Hand-authored: `dotnet ef migrations add` scaffolded this empty, because the
    // AppDbContextModelSnapshot already reflects the target model (no more of these six
    // Plan columns) as of Migration A — the snapshot always tracks the C# model, not the
    // physical database, so there was no further "diff" left for EF to detect. These
    // DropColumn/DropIndex calls are exactly the ones deliberately withheld from Migration
    // A's Up() (see that migration's own comment). Only apply this after every application
    // code path has stopped reading the old columns and Phase 11's quality gates are green
    // — see docs/claude/overnight-prompt-plan-price-model-redesign-2026-07-19.md.
    /// <inheritdoc />
    public partial class DropLegacyPlanBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plans_paired_plan_id",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "PairedPlanId",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "PriceMonthly",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "PriceYearly",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "StripePriceIdMonthly",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "StripePriceIdYearly",
                table: "plans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingInterval",
                table: "plans",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "PairedPlanId",
                table: "plans",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<decimal>(
                name: "PriceMonthly",
                table: "plans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceYearly",
                table: "plans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StripePriceIdMonthly",
                table: "plans",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StripePriceIdYearly",
                table: "plans",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_plans_paired_plan_id",
                table: "plans",
                column: "PairedPlanId");

            // Data is not restored — this Down() only recreates the schema shape,
            // matching Migration A's Down() (which also doesn't reverse its data backfill).
        }
    }
}
