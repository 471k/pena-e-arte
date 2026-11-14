using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistNotifiedSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NotifiedArtistId",
                table: "waitlist_entries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "NotifiedSlotDurationMinutes",
                table: "waitlist_entries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedSlotStart",
                table: "waitlist_entries",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifiedArtistId",
                table: "waitlist_entries");

            migrationBuilder.DropColumn(
                name: "NotifiedSlotDurationMinutes",
                table: "waitlist_entries");

            migrationBuilder.DropColumn(
                name: "NotifiedSlotStart",
                table: "waitlist_entries");
        }
    }
}
