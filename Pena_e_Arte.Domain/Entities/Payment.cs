using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid          AppointmentId          { get; set; }
    public Guid          ClientId               { get; set; }
    public decimal       Amount                 { get; set; }
    public PaymentStatus Status                 { get; set; }
    public string?       StripePaymentIntentId  { get; set; }
    public string?       ClientSecret           { get; set; }
    public DateTime?     PaidAt                 { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Client      Client      { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
