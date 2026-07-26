using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <summary>
    /// Named checkpoint: the cash payment columns (Method, CashNote, CashConfirmedByUserId)
    /// were already added to the payments table by 20260611162653_SimplifyPaymentToCashAndCard.
    /// This migration is intentionally empty — the model is unchanged.
    /// </summary>
    public partial class AddCashPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
