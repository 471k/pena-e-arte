using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingDraftClaimedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "BookingDrafts",
                type: "datetime(6)",
                nullable: true);

            // C05: every draft written before this column existed was saved by its owner while signed in
            // (SaveMyBookingDraftCommand, #460), so it is claimed. Without this backfill those drafts would read
            // as unclaimed and vanish from their owners' booking forms.
            migrationBuilder.Sql("UPDATE `BookingDrafts` SET `ClaimedAt` = `CreatedAt` WHERE `ClaimedAt` IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "BookingDrafts");
        }
    }
}
