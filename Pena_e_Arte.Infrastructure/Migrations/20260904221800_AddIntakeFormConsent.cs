using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeFormConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConsentTemplateId",
                table: "intake_forms",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ConsentTextSnapshot",
                table: "intake_forms",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentedAt",
                table: "intake_forms",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentTemplateId",
                table: "intake_forms");

            migrationBuilder.DropColumn(
                name: "ConsentTextSnapshot",
                table: "intake_forms");

            migrationBuilder.DropColumn(
                name: "ConsentedAt",
                table: "intake_forms");
        }
    }
}
