using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class DesignApproval : TenantEntity
{
    public Guid                 DesignRevisionId { get; set; }
    public DesignApprovalStatus Status           { get; set; }
    public string?              ClientNotes      { get; set; }
    public DateTime?            ReviewedAt       { get; set; }

    public DesignRevision DesignRevision { get; set; } = null!;
}
