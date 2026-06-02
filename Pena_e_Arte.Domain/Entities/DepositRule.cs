namespace Pena_e_Arte.Domain.Entities;

public class DepositRule : TenantEntity
{
    public decimal? AmountFixed   { get; set; }
    public decimal? AmountPercent { get; set; }
    public bool     IsActive      { get; set; }
}
