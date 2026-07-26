namespace Pena_e_Arte.Domain.Entities;

public class SessionSplit : TenantEntity
{
    public Guid PaymentId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }

    public Payment Payment { get; set; } = null!;
}
