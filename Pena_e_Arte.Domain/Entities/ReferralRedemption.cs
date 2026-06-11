namespace Pena_e_Arte.Domain.Entities;

public class ReferralRedemption
{
    public Guid     Id               { get; init; } = Guid.NewGuid();
    public Guid     ReferralCodeId   { get; set; }
    public Guid     NewStudioId      { get; set; }
    public DateTime RedeemedAt       { get; init; } = DateTime.UtcNow;
    public bool     DiscountApplied  { get; set; }
}
