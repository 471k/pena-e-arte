using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid              AppointmentId        { get; set; }
    public Guid              ClientId             { get; set; }
    public decimal           Amount               { get; set; }
    public PaymentStatus     Status               { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method             { get; set; } = ClientPaymentMethod.Card;

    // Card (Stripe) fields — null for cash payments
    public string? StripePaymentIntentId         { get; set; }
    public string? ClientSecret                  { get; set; }

    // Cash fields
    public string? CashNote                      { get; set; }
    public Guid?   CashConfirmedByUserId         { get; set; }

    public DateTime? PaidAt                      { get; set; }

    /// <summary>
    /// How much of Amount has actually been refunded — null/0 means none. Distinguishes a
    /// partial refund from a full one when Status is Refunded (there is no separate
    /// "PartiallyRefunded" status); revenue reporting subtracts this from Amount rather than
    /// treating any Refunded payment as contributing zero.
    /// </summary>
    public decimal? RefundedAmount                { get; set; }

    public Appointment Appointment               { get; set; } = null!;
    public Client      Client                    { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
