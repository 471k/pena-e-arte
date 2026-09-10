using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistBoothRentGiftCardsPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "artists",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booth_rent_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AmountFixed = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Frequency = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextChargeDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booth_rent_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "fk_booth_rent_schedules_artists",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "gift_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitialBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PurchaserEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderReferenceId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientSecret = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_cards", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_packages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ClientId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    GuestName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GuestEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GuestPhone = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreferredDateFrom = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PreferredDateTo = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.Id);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_artists",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_clients",
                        column: x => x.ClientId,
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "booth_rent_charges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BoothRentScheduleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ChargedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsSettled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SettledNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booth_rent_charges", x => x.Id);
                    table.ForeignKey(
                        name: "fk_booth_rent_charges_artists",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_booth_rent_charges_schedules",
                        column: x => x.BoothRentScheduleId,
                        principalTable: "booth_rent_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "package_purchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PackageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SessionsRemaining = table.Column<int>(type: "int", nullable: false),
                    ProviderReferenceId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientSecret = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_purchases", x => x.Id);
                    table.ForeignKey(
                        name: "fk_package_purchases_clients",
                        column: x => x.ClientId,
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_package_purchases_packages",
                        column: x => x.PackageId,
                        principalTable: "packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_charges_ArtistId",
                table: "booth_rent_charges",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_charges_BoothRentScheduleId",
                table: "booth_rent_charges",
                column: "BoothRentScheduleId");

            migrationBuilder.CreateIndex(
                name: "ix_booth_rent_charges_studio_id",
                table: "booth_rent_charges",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_charges_StudioId_ArtistId",
                table: "booth_rent_charges",
                columns: new[] { "StudioId", "ArtistId" });

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_charges_StudioId_IsSettled",
                table: "booth_rent_charges",
                columns: new[] { "StudioId", "IsSettled" });

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_schedules_ArtistId",
                table: "booth_rent_schedules",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "ix_booth_rent_schedules_studio_id",
                table: "booth_rent_schedules",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_booth_rent_schedules_StudioId_ArtistId",
                table: "booth_rent_schedules",
                columns: new[] { "StudioId", "ArtistId" });

            migrationBuilder.CreateIndex(
                name: "ix_gift_cards_studio_id",
                table: "gift_cards",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "ux_gift_cards_studio_code",
                table: "gift_cards",
                columns: new[] { "StudioId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_purchases_ClientId",
                table: "package_purchases",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_package_purchases_ConfirmedAt",
                table: "package_purchases",
                column: "ConfirmedAt");

            migrationBuilder.CreateIndex(
                name: "IX_package_purchases_PackageId",
                table: "package_purchases",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "ix_package_purchases_studio_id",
                table: "package_purchases",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_package_purchases_StudioId_ClientId",
                table: "package_purchases",
                columns: new[] { "StudioId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "ix_packages_studio_id",
                table: "packages",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_ArtistId",
                table: "waitlist_entries",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_ClientId",
                table: "waitlist_entries",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_studio_id",
                table: "waitlist_entries",
                column: "StudioId");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_StudioId_ArtistId_Status",
                table: "waitlist_entries",
                columns: new[] { "StudioId", "ArtistId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_StudioId_Status",
                table: "waitlist_entries",
                columns: new[] { "StudioId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booth_rent_charges");

            migrationBuilder.DropTable(
                name: "gift_cards");

            migrationBuilder.DropTable(
                name: "package_purchases");

            migrationBuilder.DropTable(
                name: "waitlist_entries");

            migrationBuilder.DropTable(
                name: "booth_rent_schedules");

            migrationBuilder.DropTable(
                name: "packages");

            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "artists");
        }
    }
}
