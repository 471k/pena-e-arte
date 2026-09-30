using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 1. New columns, nullable first (backfilled below, then locked to NOT NULL). ---

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "studios",
                type: "char(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "studios",
                type: "char(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "gift_cards",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "booth_rent_charges",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "package_purchases",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "package_purchases",
                type: "decimal(18,4)",
                nullable: true);

            // --- 2. Widen every Flow A money column to decimal(18,4) (safe: same numeric value,
            //        more room). Flow B / percent columns are untouched. ---

            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "studio_join_invites",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "session_splits",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "services",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "services",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "promo_codes",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "packages",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingBalance",
                table: "gift_cards",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "InitialBalance",
                table: "gift_cards",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "designs",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "deposit_rules",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "booth_rent_schedules",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "booth_rent_charges",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "artists",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "appointments",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            // --- 3. Backfill. Every statement is guarded (WHERE col IS NULL, or an explicit
            //        "still labelled ALL and not a real POK charge" predicate for payments) so a
            //        second run of this migration changes zero rows. ---

            // Every studio so far is in Albania (Timezone defaults to Europe/Tirane, POK is
            // Albania-only); a migration has no network to reverse-geocode. CountryCode only
            // drives the DEFAULT currency, which existing studios override to EUR below anyway.
            // The runbook (docs/payments/runbook-studio-currency-migration-2026-09-27.md) lists
            // any studio whose coordinates fall outside Albania so an admin can check it.
            migrationBuilder.Sql("UPDATE studios SET CountryCode = 'AL' WHERE CountryCode IS NULL;");

            // Every price in every existing studio was typed into a field labelled €.
            migrationBuilder.Sql("UPDATE studios SET Currency = 'EUR' WHERE Currency IS NULL;");

            // Real POK card charges keep their true currency (ALL — POK is Albania-only today);
            // every other payment (cash, or pre-POK rows) is relabelled from the wrong default to
            // the currency it was actually quoted and collected in.
            migrationBuilder.Sql(
                """
                UPDATE payments
                   SET Currency = 'EUR'
                 WHERE Currency = 'ALL'
                   AND NOT (Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status IN ('Captured', 'Paid', 'Refunded'));
                """);

            // Gift cards actually sent to POK (and past Pending) were quoted in ALL; everything
            // else — including ones never sent to a provider — was priced in €.
            migrationBuilder.Sql(
                """
                UPDATE gift_cards
                   SET Currency = 'ALL'
                 WHERE Currency IS NULL
                   AND Provider = 'pok'
                   AND ProviderReferenceId IS NOT NULL
                   AND Status <> 'Pending';
                """);
            migrationBuilder.Sql("UPDATE gift_cards SET Currency = 'EUR' WHERE Currency IS NULL;");

            // Booth rent never touches a card provider — always the studio's (now EUR) currency.
            migrationBuilder.Sql("UPDATE booth_rent_charges SET Currency = 'EUR' WHERE Currency IS NULL;");

            // Package purchases confirmed through POK were sent as ALL; everything else was €.
            migrationBuilder.Sql(
                """
                UPDATE package_purchases
                   SET Currency = 'ALL'
                 WHERE Currency IS NULL
                   AND ConfirmedAt IS NOT NULL
                   AND Provider = 'pok';
                """);
            migrationBuilder.Sql("UPDATE package_purchases SET Currency = 'EUR' WHERE Currency IS NULL;");

            // PackagePurchase stored no amount at all before tonight; snapshot the price the
            // client actually paid from the package as it stands today (the closest available
            // truth — see docs/claude/architecture.md Decisions Log, "Studio currency").
            //
            // Drift from the overnight prompt's literal §2.13 SQL ("WHERE pp.Amount = 0"): the
            // column was just added NULLABLE (step 1 above), so every existing row's Amount is
            // NULL, not 0 — "= 0" would never match them and the NOT NULL lock below would then
            // fail on those still-NULL rows. IS NULL is the correct/idempotent guard for a
            // column that started nullable; "= 0" is kept as a second, harmless guard in case a
            // caller ever legitimately re-runs this against a zero-priced package purchase.
            migrationBuilder.Sql(
                """
                UPDATE package_purchases pp
                  JOIN packages p ON p.Id = pp.PackageId
                   SET pp.Amount = p.Price
                 WHERE pp.Amount IS NULL OR pp.Amount = 0;
                """);

            // --- 4. Lock the new columns down now that every row has a value. ---

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "studios",
                type: "char(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(2)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "studios",
                type: "char(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(3)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "gift_cards",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(3)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "booth_rent_charges",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(3)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "package_purchases",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(3)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "package_purchases",
                type: "decimal(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            // --- 5. Drop the DEFAULT 'ALL' left on payments.Currency by migration
            //        20260731194633. EF's model snapshot never recorded that default (it was set
            //        via AddColumn's raw defaultValue, not HasDefaultValue), so AlterColumn above
            //        would never emit a DROP DEFAULT — it has to be raw SQL. A missing Currency on
            //        a new payment must now fail loudly (NOT NULL, no default), not silently
            //        become lek. ---

            migrationBuilder.Sql("ALTER TABLE payments ALTER COLUMN Currency DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This Down() reverses the schema only. It deliberately does NOT restore payments'
            // DEFAULT 'ALL', and does not attempt to un-relabel any row from EUR back to ALL —
            // the old 'ALL' labels were simply wrong (every price was typed into a field labelled
            // €), so there is nothing correct to roll back to.

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "gift_cards");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "booth_rent_charges");

            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "studio_join_invites",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "session_splits",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "services",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "services",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "promo_codes",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "packages",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingBalance",
                table: "gift_cards",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "InitialBalance",
                table: "gift_cards",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "designs",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "deposit_rules",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountFixed",
                table: "booth_rent_schedules",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "booth_rent_charges",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "HourlyRate",
                table: "artists",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DepositAmount",
                table: "appointments",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)");

            // Restore the payments.Currency DEFAULT 'ALL' that existed before this migration, so
            // Down() leaves the schema exactly as migration 20260731194633 left it.
            migrationBuilder.Sql("ALTER TABLE payments ALTER COLUMN Currency SET DEFAULT 'ALL';");
        }
    }
}
