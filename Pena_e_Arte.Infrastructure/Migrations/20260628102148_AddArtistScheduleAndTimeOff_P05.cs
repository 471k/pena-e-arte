using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistScheduleAndTimeOff_P05 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the composite index first so MySQL accepts dropping the FK-backing single-column index
            migrationBuilder.CreateIndex(
                name: "ix_appointments_artist_date_enddate_status",
                table: "appointments",
                columns: new[] { "ArtistId", "Date", "EndDate", "Status" });

            migrationBuilder.DropIndex(
                name: "IX_appointments_ArtistId",
                table: "appointments");

            migrationBuilder.RenameIndex(
                name: "IX_PortfolioImages_ArtistId",
                table: "PortfolioImages",
                newName: "ix_portfolio_images_artist_id");

            migrationBuilder.AddColumn<DateTime>(
                name: "AftercareSentAt",
                table: "appointments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "appointments",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReminderJobId24h",
                table: "appointments",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReminderJobId48h",
                table: "appointments",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ArtistSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    IsAvailable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistSchedules_artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ArtistTimeOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ArtistId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StudioId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistTimeOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistTimeOffs_artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_studio_created_at",
                table: "notification_logs",
                columns: new[] { "StudioId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_artist_schedule_artist_id",
                table: "ArtistSchedules",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "uix_artist_schedule_artist_day",
                table: "ArtistSchedules",
                columns: new[] { "ArtistId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_artist_time_off_artist_dates",
                table: "ArtistTimeOffs",
                columns: new[] { "ArtistId", "StartDate", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistSchedules");

            migrationBuilder.DropTable(
                name: "ArtistTimeOffs");

            migrationBuilder.DropIndex(
                name: "ix_notification_logs_studio_created_at",
                table: "notification_logs");

            migrationBuilder.DropIndex(
                name: "ix_appointments_artist_date_enddate_status",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "AftercareSentAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "ReminderJobId24h",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "ReminderJobId48h",
                table: "appointments");

            migrationBuilder.RenameIndex(
                name: "ix_portfolio_images_artist_id",
                table: "PortfolioImages",
                newName: "IX_PortfolioImages_ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_ArtistId",
                table: "appointments",
                column: "ArtistId");
        }
    }
}
