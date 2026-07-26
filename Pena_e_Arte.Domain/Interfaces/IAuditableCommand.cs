namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Marks a MediatR command whose successful execution must be recorded in the
/// structured audit log. Picked up by AuditLogBehavior in the Application layer's
/// MediatR pipeline (registered after PlanLimitBehavior in Program.cs), which logs
/// ONLY after the handler (and its SaveChangesAsync) completes without throwing —
/// mirrors PlanLimitBehavior's own "IQuotaCheckedCommand" precedent.
/// See docs/claude/architecture.md Decisions Log — "Structured Admin/Audit Log".
/// </summary>
public interface IAuditableCommand
{
    string AuditAction { get; }
    string AuditTargetType { get; }
    Guid AuditTargetId { get; }

    /// <summary>
    /// Explicit studio target when the command carries one directly (e.g. issuer
    /// commands with a StudioId property). Null falls back to the caller's own
    /// ICurrentTenant.StudioId when set (tenant-scoped commands), or to null for
    /// genuinely platform-wide actions / commands that don't expose a resolvable
    /// studio id (see AuditLogBehavior).
    /// </summary>
    Guid? AuditStudioId => null;
}
