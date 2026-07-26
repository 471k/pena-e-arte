using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPriceAndSubscriptionBillingInterval : Migration
    {
        // Deliberate deviation from what `dotnet ef migrations add` scaffolded: the
        // six now-dead `plans` columns (BillingInterval, PriceMonthly, PriceYearly,
        // StripePriceIdMonthly, StripePriceIdYearly, PairedPlanId) and their index are
        // NOT dropped here. `Plan.cs` no longer maps them, so EF Core wants to drop them
        // in this same migration — but the raw-SQL data backfill below reads the OLD
        // physical columns (which still exist in the database, just not in the C#
        // model) to populate `plan_prices`. Dropping them now would destroy the data
        // the backfill needs. They move to the separate `DropLegacyPlanBillingFields`
        // migration, applied only once every application code path has stopped reading
        // them. See docs/claude/overnight-prompt-plan-price-model-redesign-2026-07-19.md
        // (Phase 3 / "Design decisions" #1-2) and architecture.md Decisions Log —
        // "Plan/PlanPrice split".
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingInterval",
                table: "subscriptions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Monthly")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PendingBillingInterval",
                table: "subscriptions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "plan_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Interval = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StripePriceId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_prices", x => x.Id);
                    table.ForeignKey(
                        name: "fk_plan_prices_plans",
                        column: x => x.PlanId,
                        principalTable: "plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ux_plan_prices_plan_id_interval",
                table: "plan_prices",
                columns: new[] { "PlanId", "Interval" },
                unique: true);

            // ─── Data backfill ────────────────────────────────────────────────────
            //
            // Precondition (see overnight prompt, "Precondition — read this before
            // touching anything"): DataSeeder.ReconcileCorePlansAsync and
            // RetireOrphanedNamedPlansAsync have already run at least once against this
            // database, so `plans` is guaranteed to contain exactly six rows — Free,
            // Starter, Growth, Premium x2 (PremiumMonthlyPlanId / PremiumYearlyPlanId),
            // Pro — with no orphans. GUID literals below are DataSeeder's fixed platform
            // constants, not tenant data, so hardcoding them here is fine.

            // 5a. One Monthly PlanPrice for every canonical plan except the redundant
            //     Yearly Premium row (handled in 5b/5c — it doesn't survive as its own
            //     Plan row).
            migrationBuilder.Sql("""
                INSERT INTO plan_prices (Id, PlanId, `Interval`, Price, StripePriceId, IsActive)
                SELECT UUID(), Id, 'Monthly', PriceMonthly, StripePriceIdMonthly, 1
                FROM plans
                WHERE Id <> 'aaaa0005-0000-0000-0000-000000000000';
                """);

            // 5b. The surviving Premium row's Yearly PlanPrice — sourced from the
            //     YEARLY row's own PriceYearly/StripePriceIdYearly (the fields actually
            //     gated live by that row's BillingInterval = 'Yearly' today), NOT
            //     PremiumMonthlyPlanId's own decorative "reference only" fields, which
            //     could have drifted independently per UpdatePlanHandler's pairing-sync
            //     exclusion.
            migrationBuilder.Sql("""
                INSERT INTO plan_prices (Id, PlanId, `Interval`, Price, StripePriceId, IsActive)
                SELECT UUID(), 'aaaa0004-0000-0000-0000-000000000000', 'Yearly', PriceYearly, StripePriceIdYearly, 1
                FROM plans
                WHERE Id = 'aaaa0005-0000-0000-0000-000000000000';
                """);

            // 5c. Reassign every subscription (active AND pending) off the redundant
            //     Yearly row onto the surviving Monthly row, recording that they're
            //     actually billed yearly.
            migrationBuilder.Sql("""
                UPDATE subscriptions
                SET PlanId = 'aaaa0004-0000-0000-0000-000000000000', BillingInterval = 'Yearly'
                WHERE PlanId = 'aaaa0005-0000-0000-0000-000000000000';
                """);
            migrationBuilder.Sql("""
                UPDATE subscriptions
                SET PendingPlanId = 'aaaa0004-0000-0000-0000-000000000000', PendingBillingInterval = 'Yearly'
                WHERE PendingPlanId = 'aaaa0005-0000-0000-0000-000000000000';
                """);

            // 5d. Delete the now-redundant Yearly row — its price data was copied in
            //     5b, every subscription was moved off it in 5c.
            migrationBuilder.Sql("""
                DELETE FROM plans WHERE Id = 'aaaa0005-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schema-only reversal — the Premium row merge / subscription reassignment
            // performed by the data backfill above is not undone (same as any other
            // data-migrating Up(), consistent with this codebase's existing migrations).
            migrationBuilder.DropTable(
                name: "plan_prices");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PendingBillingInterval",
                table: "subscriptions");
        }
    }
}
