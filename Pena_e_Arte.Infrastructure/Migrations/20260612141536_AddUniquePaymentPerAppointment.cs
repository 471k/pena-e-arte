using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <summary>
    /// Enforces one payment row per appointment at the database level — the
    /// application-level duplicate checks alone are racy (concurrent card-intent
    /// creation vs cash declaration). Handlers convert existing rows in place.
    /// The unique index is created before the old index is dropped because MySQL
    /// requires the AppointmentId foreign key to be backed by an index at all times.
    /// </summary>
    public partial class AddUniquePaymentPerAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: collapse any duplicate payments (keep the most recent row,
            // which reflects the latest client intent) so the unique index can apply.
            migrationBuilder.Sql(
                """
                DELETE p1 FROM payments p1
                JOIN payments p2
                  ON p1.AppointmentId = p2.AppointmentId
                 AND (p1.CreatedAt < p2.CreatedAt OR (p1.CreatedAt = p2.CreatedAt AND p1.Id < p2.Id));
                """);

            migrationBuilder.CreateIndex(
                name: "ux_payments_appointment_id",
                table: "payments",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_payments_AppointmentId",
                table: "payments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_payments_AppointmentId",
                table: "payments",
                column: "AppointmentId");

            migrationBuilder.DropIndex(
                name: "ux_payments_appointment_id",
                table: "payments");
        }
    }
}
