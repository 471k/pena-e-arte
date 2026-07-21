using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationPolicyToDepositRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CancellationWindowHours",
                table: "deposit_rules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundPercentOnLateCancel",
                table: "deposit_rules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationWindowHours",
                table: "deposit_rules");

            migrationBuilder.DropColumn(
                name: "RefundPercentOnLateCancel",
                table: "deposit_rules");
        }
    }
}
