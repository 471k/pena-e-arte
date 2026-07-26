using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioImageStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "PortfolioImages",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SavedPortfolioImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PortfolioImageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SavedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPortfolioImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedPortfolioImages_PortfolioImages_PortfolioImageId",
                        column: x => x.PortfolioImageId,
                        principalTable: "PortfolioImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPortfolioImages_PortfolioImageId",
                table: "SavedPortfolioImages",
                column: "PortfolioImageId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPortfolioImages_UserId_PortfolioImageId",
                table: "SavedPortfolioImages",
                columns: new[] { "UserId", "PortfolioImageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedPortfolioImages");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "PortfolioImages");
        }
    }
}
