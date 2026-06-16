using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRecipientType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows predate the RecipientType column. RecipientId was always either
            // a Client.Id (appointment confirmation/cancellation/reminder notifications) or a
            // Studio.Id (trial expiry warnings) — default everything to Client, then correct
            // any row whose RecipientId actually matches a Studio.
            migrationBuilder.AddColumn<string>(
                name: "RecipientType",
                table: "notification_logs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Client")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE notification_logs nl
                INNER JOIN studios s ON s.Id = nl.RecipientId
                SET nl.RecipientType = 'Studio';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientType",
                table: "notification_logs");
        }
    }
}
