using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingPlanIdToSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PendingPlanId",
                table: "subscriptions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_PendingPlanId",
                table: "subscriptions",
                column: "PendingPlanId");

            migrationBuilder.AddForeignKey(
                name: "fk_subscriptions_pending_plans",
                table: "subscriptions",
                column: "PendingPlanId",
                principalTable: "plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscriptions_pending_plans",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_PendingPlanId",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PendingPlanId",
                table: "subscriptions");
        }
    }
}
