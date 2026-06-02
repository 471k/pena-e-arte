namespace Pena_e_Arte.Domain.Entities;

public class DesignRevision : TenantEntity
{
    public Guid    DesignId      { get; set; }
    public int     VersionNumber { get; set; }
    public string  FileUrl       { get; set; } = string.Empty;
    public string? Notes         { get; set; }
    public DateTime UploadedAt   { get; set; } = DateTime.UtcNow;

    public Design          Design   { get; set; } = null!;
    public DesignApproval? Approval { get; set; }
}
