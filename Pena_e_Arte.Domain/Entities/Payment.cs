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

    public Appointment Appointment               { get; set; } = null!;
    public Client      Client                    { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
