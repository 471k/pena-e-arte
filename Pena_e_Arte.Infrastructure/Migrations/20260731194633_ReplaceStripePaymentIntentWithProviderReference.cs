using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStripePaymentIntentWithProviderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StripePaymentIntentId",
                table: "payments",
                newName: "ProviderReferenceId");

            // Backfill existing rows to ALL (Albanian lek) — the entity default and the only
            // currency in use before this column existed — not an empty string.
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "payments",
                type: "varchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "ALL")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresAt",
                table: "payments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeeAmount",
                table: "payments",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "payments",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PlatformFeeAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "payments");

            migrationBuilder.RenameColumn(
                name: "ProviderReferenceId",
                table: "payments",
                newName: "StripePaymentIntentId");
        }
    }
}
