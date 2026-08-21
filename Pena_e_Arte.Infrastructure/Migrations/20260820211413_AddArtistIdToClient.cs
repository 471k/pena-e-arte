using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistIdToClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArtistId",
                table: "clients",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_clients_ArtistId",
                table: "clients",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "ix_clients_studio_artist",
                table: "clients",
                columns: new[] { "StudioId", "ArtistId" });

            migrationBuilder.AddForeignKey(
                name: "fk_clients_artists",
                table: "clients",
                column: "ArtistId",
                principalTable: "artists",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clients_artists",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "IX_clients_ArtistId",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "ix_clients_studio_artist",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "ArtistId",
                table: "clients");
        }
    }
}
