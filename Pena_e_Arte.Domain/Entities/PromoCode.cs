namespace Pena_e_Arte.Domain.Entities;

public class PromoCode : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public decimal? AmountFixed { get; set; }
    public decimal? AmountPercent { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool IsActive { get; set; }
}
