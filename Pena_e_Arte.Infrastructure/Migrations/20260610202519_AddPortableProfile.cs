using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortableProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowCrossTenantRead",
                table: "client_profiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CrossTenantOptInAt",
                table: "client_profiles",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_profiles_allow_cross_tenant_read",
                table: "client_profiles",
                column: "AllowCrossTenantRead");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_client_profiles_allow_cross_tenant_read",
                table: "client_profiles");

            migrationBuilder.DropColumn(
                name: "AllowCrossTenantRead",
                table: "client_profiles");

            migrationBuilder.DropColumn(
                name: "CrossTenantOptInAt",
                table: "client_profiles");
        }
    }
}
