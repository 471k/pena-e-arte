namespace Pena_e_Arte.Domain.Entities;

public class DesignShareToken : TenantEntity
{
    public string   Token            { get; set; } = string.Empty;
    public Guid     DesignRevisionId { get; set; }
    public Guid     CreatedByUserId  { get; set; }
    public DateTime ExpiresAt        { get; set; }
    public bool     IsRevoked        { get; set; }
    public int      ViewCount        { get; set; }

    public DesignRevision DesignRevision { get; set; } = null!;
}
