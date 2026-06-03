namespace Pena_e_Arte.Domain.Entities;

public class DepositRule : TenantEntity
{
    public string   Name          { get; set; } = string.Empty;
    public decimal? AmountFixed   { get; set; }
    public decimal? AmountPercent { get; set; }
    public bool     IsActive      { get; set; }
}
