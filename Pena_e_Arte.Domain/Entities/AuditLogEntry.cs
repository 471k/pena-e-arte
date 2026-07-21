namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Append-only record of a trust/compliance-sensitive action. Deliberately NOT a
/// TenantEntity: StudioId is nullable (null = platform-wide action with no single
/// studio target, e.g. Plan.Updated), and there is no EF Core global query filter —
/// authorization for who may read which rows is enforced in the query handlers
/// (GetAuditLogHandler for the issuer, GetMyStudioAuditLogHandler for the owner),
/// same non-tenant-scoped shape as FeedbackReport/UserOnboardingState.
/// </summary>
public class AuditLogEntry
{
    private AuditLogEntry() { }

    public Guid     Id          { get; private set; } = Guid.NewGuid();
    public Guid     ActorUserId { get; private set; }
    public string   ActorRole   { get; private set; } = string.Empty;
    public string   Action      { get; private set; } = string.Empty;
    public string   TargetType  { get; private set; } = string.Empty;
    public Guid     TargetId    { get; private set; }
    public Guid?    StudioId    { get; private set; }

    /// <summary>Whitelisted, PII-scrubbed JSON — never names/emails/phone numbers/free text. See AuditMetadataBuilder.</summary>
    public string   Metadata    { get; private set; } = "{}";
    public DateTime CreatedAt   { get; private set; } = DateTime.UtcNow;

    public static AuditLogEntry Create(
        Guid actorUserId, string actorRole, string action, string targetType,
        Guid targetId, Guid? studioId, string metadata) =>
        new()
        {
            ActorUserId = actorUserId,
            ActorRole   = actorRole,
            Action      = action,
            TargetType  = targetType,
            TargetId    = targetId,
            StudioId    = studioId,
            Metadata    = metadata,
        };
}
