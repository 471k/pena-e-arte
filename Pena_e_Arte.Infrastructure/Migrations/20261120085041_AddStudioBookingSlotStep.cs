using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioBookingSlotStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookingSlotStepMinutes",
                table: "studios",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingSlotStepMinutes",
                table: "studios");
        }
    }
}
