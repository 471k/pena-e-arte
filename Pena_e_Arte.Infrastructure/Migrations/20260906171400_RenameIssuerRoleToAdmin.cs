using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameIssuerRoleToAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IssuerNote",
                table: "FeedbackReports",
                newName: "AdminNote");

            // Renamed in place (not deleted+recreated) so any environment that already has a
            // user assigned to this RoleId — e.g. local dev's seeded issuer account — keeps
            // that assignment intact automatically, with no re-seeding required.
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'admin', NormalizedName = 'ADMIN' WHERE Name = 'issuer';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name = 'issuer', NormalizedName = 'ISSUER' WHERE Name = 'admin';");

            migrationBuilder.RenameColumn(
                name: "AdminNote",
                table: "FeedbackReports",
                newName: "IssuerNote");
        }
    }
}
