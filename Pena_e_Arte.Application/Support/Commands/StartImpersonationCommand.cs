using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Support.Commands;

/// <summary>
/// Mints a Support Impersonation session + JWT for an AdminOnly caller. See
/// docs/claude/architecture.md Decisions Log — "Support Impersonation with Audit Trail"
/// for the full design, and TenantMiddleware for the allow-list gate that is the actual
/// enforcement mechanism (the "admin" role itself already satisfies every RBAC policy in
/// this codebase — see AuthorizationExtensions.cs).
/// </summary>
public record StartImpersonationCommand(Guid StudioId, StartImpersonationRequest Request)
    : IRequest<ImpersonationTokenResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.ImpersonationSessionStarted;
    public string AuditTargetType => AuditTargetTypes.ImpersonationSession;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;
}

public class StartImpersonationHandler(IAppDbContext db, ICurrentUser currentUser, IIdentityService identity)
    : IRequestHandler<StartImpersonationCommand, ImpersonationTokenResponse>
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(45);

    public async Task<ImpersonationTokenResponse> Handle(StartImpersonationCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #52 — AdminOnly cross-tenant studio lookup to
        // start an impersonation session. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        ImpersonationSession session = ImpersonationSession.Start(
            currentUser.UserId, command.StudioId, command.Request.ReasonCode, SessionDuration);

        db.ImpersonationSessions.Add(session);
        await db.SaveChangesAsync(ct);

        (bool success, string? accessToken, string? error) = await identity.IssueImpersonationTokenAsync(
            currentUser.UserId, command.StudioId, session.Id, session.ExpiresAt);

        if (!success || accessToken is null)
            throw new BusinessRuleViolationException(error ?? "Failed to start impersonation session.");

        return new ImpersonationTokenResponse(
            session.Id, accessToken, session.ExpiresAt, studio.Id, studio.Name);
    }
}

public class StartImpersonationValidator : AbstractValidator<StartImpersonationCommand>
{
    public StartImpersonationValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Request.ReasonCode).NotEmpty().MinimumLength(5).MaximumLength(500);
    }
}
