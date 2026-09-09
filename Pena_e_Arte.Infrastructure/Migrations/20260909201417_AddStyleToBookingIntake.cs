using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStyleToBookingIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "booking_intakes",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Style",
                table: "booking_intakes");
        }
    }
}
