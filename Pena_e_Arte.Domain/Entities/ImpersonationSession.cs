namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A time-boxed grant letting a platform admin browse a studio's own data under that
/// studio's tenant scope. A TenantEntity with StudioId = the TARGET studio (not the
/// platform) — this is deliberately a record that belongs to the target studio's own
/// audit trail as much as the platform's, matching every other audit-adjacent entity's
/// shape in this codebase (see AppDbContext / architecture.md Decisions Log).
///
/// The row itself is NOT the enforcement mechanism — the "imp" JWT claim plus
/// TenantMiddleware's per-request allow-list check is (see docs/claude/architecture.md).
/// This row exists so that check can be revoked immediately (EndedAt) and so the
/// session is independently expirable (ExpiresAt) without trusting the JWT's own "exp"
/// alone — a JWT cannot be revoked by a DB update unless something checks a DB row on
/// every request, which TenantMiddleware does.
/// </summary>
public class ImpersonationSession : TenantEntity
{
    private ImpersonationSession() { }

    /// <summary>The real admin. Never overwritten by the impersonated context — every
    /// action taken during the session is attributed to this id in the audit log.</summary>
    public Guid ActorUserId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public bool IsActive => EndedAt is null && ExpiresAt > DateTime.UtcNow;

    public static ImpersonationSession Start(
        Guid actorUserId, Guid targetStudioId, string reasonCode, TimeSpan duration) =>
        new()
        {
            StudioId = targetStudioId,
            ActorUserId = actorUserId,
            ReasonCode = reasonCode,
            ExpiresAt = DateTime.UtcNow.Add(duration),
        };

    public void End() => EndedAt ??= DateTime.UtcNow;
}
