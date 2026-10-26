using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyReferenceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceAmount",
                table: "payments",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCurrency",
                table: "payments",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceRate",
                table: "payments",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReferenceRateDate",
                table: "payments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceRateSource",
                table: "payments",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceAmount",
                table: "package_purchases",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCurrency",
                table: "package_purchases",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceRate",
                table: "package_purchases",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReferenceRateDate",
                table: "package_purchases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceRateSource",
                table: "package_purchases",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceAmount",
                table: "gift_cards",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCurrency",
                table: "gift_cards",
                type: "varchar(3)",
                maxLength: 3,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceRate",
                table: "gift_cards",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReferenceRateDate",
                table: "gift_cards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceRateSource",
                table: "gift_cards",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceCurrency",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceRate",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceRateDate",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceRateSource",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferenceAmount",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "ReferenceCurrency",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "ReferenceRate",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "ReferenceRateDate",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "ReferenceRateSource",
                table: "package_purchases");

            migrationBuilder.DropColumn(
                name: "ReferenceAmount",
                table: "gift_cards");

            migrationBuilder.DropColumn(
                name: "ReferenceCurrency",
                table: "gift_cards");

            migrationBuilder.DropColumn(
                name: "ReferenceRate",
                table: "gift_cards");

            migrationBuilder.DropColumn(
                name: "ReferenceRateDate",
                table: "gift_cards");

            migrationBuilder.DropColumn(
                name: "ReferenceRateSource",
                table: "gift_cards");
        }
    }
}
