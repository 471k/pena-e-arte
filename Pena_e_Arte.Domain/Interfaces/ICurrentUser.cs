namespace Pena_e_Arte.Domain.Interfaces;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Role { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>True when the current request's JWT carries an "imp" claim — i.e. this
    /// is a platform admin acting under an active Support Impersonation session, not a
    /// normal admin request. See AuditLogBehavior, which uses this to record
    /// "admin-impersonating" instead of the raw "admin" role on every audited action.</summary>
    bool IsImpersonating { get; }
}
