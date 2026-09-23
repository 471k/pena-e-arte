using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBilledAmountSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BilledCurrency",
                table: "subscriptions",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BilledQuantity",
                table: "subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BilledUnitAmount",
                table: "subscriptions",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecurringDiscountPercent",
                table: "subscriptions",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_invoice_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StripeInvoiceId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AmountPaid = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_invoice_payments", x => x.Id);
                    table.ForeignKey(
                        name: "fk_subscription_invoice_payments_subscriptions",
                        column: x => x.SubscriptionId,
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoice_payments_paid_at",
                table: "subscription_invoice_payments",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoice_payments_stripe_invoice_id",
                table: "subscription_invoice_payments",
                column: "StripeInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_invoice_payments_studio_id",
                table: "subscription_invoice_payments",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoice_payments_SubscriptionId",
                table: "subscription_invoice_payments",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_invoice_payments");

            migrationBuilder.DropColumn(
                name: "BilledCurrency",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "BilledQuantity",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "BilledUnitAmount",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "RecurringDiscountPercent",
                table: "subscriptions");
        }
    }
}
