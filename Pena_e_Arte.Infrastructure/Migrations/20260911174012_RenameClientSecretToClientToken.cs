using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameClientSecretToClientToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ClientSecret",
                table: "payments",
                newName: "ClientToken");

            migrationBuilder.RenameColumn(
                name: "ClientSecret",
                table: "package_purchases",
                newName: "ClientToken");

            migrationBuilder.RenameColumn(
                name: "ClientSecret",
                table: "gift_cards",
                newName: "ClientToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ClientToken",
                table: "payments",
                newName: "ClientSecret");

            migrationBuilder.RenameColumn(
                name: "ClientToken",
                table: "package_purchases",
                newName: "ClientSecret");

            migrationBuilder.RenameColumn(
                name: "ClientToken",
                table: "gift_cards",
                newName: "ClientSecret");
        }
    }
}
