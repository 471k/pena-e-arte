using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistPayModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A07a: additive only. No rate is backfilled (null is the point: an unset rate is unknown,
            // not a guess), and PayModelReviewedAt stays null for every existing studio, including one
            // whose artists already have a CommissionRate: nobody has confirmed it under the new model.
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultCommissionRate",
                table: "studios",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultFlatFeePerSession",
                table: "studios",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultPayArrangement",
                table: "studios",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Commission")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PayModelReviewedAt",
                table: "studios",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FlatFeePerSession",
                table: "artists",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayArrangement",
                table: "artists",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCommissionRate",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "DefaultFlatFeePerSession",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "DefaultPayArrangement",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "PayModelReviewedAt",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "FlatFeePerSession",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "PayArrangement",
                table: "artists");
        }
    }
}
