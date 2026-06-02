using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Plan
{
    public Guid            Id                    { get; init; } = Guid.NewGuid();
    public string          Name                  { get; set; }  = string.Empty;
    public BillingInterval BillingInterval       { get; set; }
    public decimal         PriceMonthly          { get; set; }
    public decimal         PriceYearly           { get; set; }
    public int             YearlyDiscountPercent { get; set; }  = 17;
    public DateTime        CreatedAt             { get; init; } = DateTime.UtcNow;

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
