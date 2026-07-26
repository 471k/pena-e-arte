using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentIdToReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_AuthorUserId_ArtistId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_AuthorUserId_StudioId",
                table: "Reviews");

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "Reviews",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AppointmentId",
                table: "Reviews",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AppointmentId_ArtistId",
                table: "Reviews",
                columns: new[] { "AppointmentId", "ArtistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AppointmentId_StudioId",
                table: "Reviews",
                columns: new[] { "AppointmentId", "StudioId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_appointments_AppointmentId",
                table: "Reviews",
                column: "AppointmentId",
                principalTable: "appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_appointments_AppointmentId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_AppointmentId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_AppointmentId_ArtistId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_AppointmentId_StudioId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "Reviews");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AuthorUserId_ArtistId",
                table: "Reviews",
                columns: new[] { "AuthorUserId", "ArtistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_AuthorUserId_StudioId",
                table: "Reviews",
                columns: new[] { "AuthorUserId", "StudioId" },
                unique: true);
        }
    }
}
