using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Common.Behaviors;

/// <summary>
/// Logs auditable commands (those implementing IAuditableCommand) to the structured
/// audit log — but only AFTER the handler (and its own SaveChangesAsync) completes
/// without throwing. A command that fails validation or throws mid-handler must not
/// produce a misleading "this happened" audit row. Registered after PlanLimitBehavior
/// in Program.cs, mirroring that behavior's exact shape.
/// </summary>
public class AuditLogBehavior<TRequest, TResponse>(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant tenant)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        TResponse response = await next(ct);

        if (request is IAuditableCommand auditable)
        {
            Guid? studioId = auditable.AuditStudioId ?? (tenant.IsSet ? tenant.StudioId : null);

            // Distinguishes an impersonated admin's action from the admin's own direct
            // action with zero schema change — ActorRole is already a plain string, and
            // ActorUserId still correctly identifies the real admin either way (see
            // IIdentityService.IssueImpersonationTokenAsync). See architecture.md Decisions
            // Log — "Support Impersonation with Audit Trail".
            string actorRole = currentUser.IsImpersonating ? "admin-impersonating" : currentUser.Role;

            AuditLogEntry entry = AuditLogEntry.Create(
                actorUserId: currentUser.UserId,
                actorRole: actorRole,
                action: auditable.AuditAction,
                targetType: auditable.AuditTargetType,
                targetId: auditable.AuditTargetId,
                studioId: studioId,
                metadata: AuditMetadataBuilder.Build(request));

            db.AuditLogEntries.Add(entry);
            await db.SaveChangesAsync(ct);
        }

        return response;
    }
}
