namespace Pena_e_Arte.Domain.Entities;

public class Package : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
