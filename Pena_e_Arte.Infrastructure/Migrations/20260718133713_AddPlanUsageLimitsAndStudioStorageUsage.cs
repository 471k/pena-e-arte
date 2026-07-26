using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanUsageLimitsAndStudioStorageUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StorageUsageBytes",
                table: "studios",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "AllowApiAccess",
                table: "plans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxAppointmentsPerMonth",
                table: "plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxArtists",
                table: "plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLocations",
                table: "plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxNotificationsPerMonth",
                table: "plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxStorageGb",
                table: "plans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PairedPlanId",
                table: "plans",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<bool>(
                name: "PrioritySupport",
                table: "plans",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_plans_paired_plan_id",
                table: "plans",
                column: "PairedPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plans_paired_plan_id",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "StorageUsageBytes",
                table: "studios");

            migrationBuilder.DropColumn(
                name: "AllowApiAccess",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "MaxAppointmentsPerMonth",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "MaxArtists",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "MaxLocations",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "MaxNotificationsPerMonth",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "MaxStorageGb",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "PairedPlanId",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "PrioritySupport",
                table: "plans");
        }
    }
}
