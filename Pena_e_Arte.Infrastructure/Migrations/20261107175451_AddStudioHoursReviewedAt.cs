using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioHoursReviewedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HoursReviewedAt",
                table: "studios",
                type: "datetime(6)",
                nullable: true);

            // E05a (O02) backfill: an owner who already saved the hours form must not be told to
            // review them. Registration writes a studio's hours rows in one request, within a second
            // of each other, so a row the owner touched is one either updated more than a minute after
            // it was created (the form updates existing days) or created more than a minute after the
            // studio's first hours row (the form adds a day it didn't have). The one-minute margin is
            // there because a never-touched seeded row can already have UpdatedAt a microsecond after
            // CreatedAt (measured on the local seed data, 7 Nov 2026), so "UpdatedAt > CreatedAt"
            // would wrongly call it reviewed. Studios with no such row stay NULL: not yet reviewed.
            migrationBuilder.Sql(@"
UPDATE studios s
JOIN (
    SELECT h.StudioId, MAX(GREATEST(h.CreatedAt, h.UpdatedAt)) AS ReviewedAt
    FROM StudioHours h
    JOIN (SELECT StudioId, MIN(CreatedAt) AS FirstAt FROM StudioHours GROUP BY StudioId) f
        ON f.StudioId = h.StudioId
    WHERE h.UpdatedAt > DATE_ADD(h.CreatedAt, INTERVAL 1 MINUTE)
       OR h.CreatedAt > DATE_ADD(f.FirstAt, INTERVAL 1 MINUTE)
    GROUP BY h.StudioId
) r ON r.StudioId = s.Id
SET s.HoursReviewedAt = r.ReviewedAt;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HoursReviewedAt",
                table: "studios");
        }
    }
}
