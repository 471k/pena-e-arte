using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoDetailToTrafficEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccuracyRadiusKm",
                table: "traffic_events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AsnNumber",
                table: "traffic_events",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AsnOrganization",
                table: "traffic_events",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Continent",
                table: "traffic_events",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContinentCode",
                table: "traffic_events",
                type: "varchar(2)",
                maxLength: 2,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "traffic_events",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "traffic_events",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "traffic_events",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RegionCode",
                table: "traffic_events",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "traffic_events",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccuracyRadiusKm",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "AsnNumber",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "AsnOrganization",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "Continent",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "ContinentCode",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "RegionCode",
                table: "traffic_events");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "traffic_events");
        }
    }
}
