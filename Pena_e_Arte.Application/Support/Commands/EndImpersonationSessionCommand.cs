using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Support.Commands;

/// <summary>
/// Ends a Support Impersonation session immediately. A JWT is self-contained and cannot be
/// revoked by a DB update alone — this only takes effect because TenantMiddleware's gate
/// looks the session up by its "imp" claim on every impersonated request and rejects once
/// EndedAt is set, rather than trusting the token's own "exp" claim alone.
/// </summary>
public record EndImpersonationSessionCommand(Guid SessionId) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.ImpersonationSessionEnded;
    public string AuditTargetType => AuditTargetTypes.ImpersonationSession;
    public Guid AuditTargetId => SessionId;
}

public class EndImpersonationSessionHandler(IAppDbContext db)
    : IRequestHandler<EndImpersonationSessionCommand>
{
    public async Task Handle(EndImpersonationSessionCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #52 — the calling admin's own JWT carries no
        // tenant_id claim (they are not impersonating — this command ends a session, it
        // isn't called from within one), so the normal tenant filter would match nothing.
        // See architecture.md.
        ImpersonationSession session = await db.ImpersonationSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == command.SessionId, ct)
            ?? throw new NotFoundException(nameof(ImpersonationSession), command.SessionId);

        session.End();
        await db.SaveChangesAsync(ct);
    }
}

public class EndImpersonationSessionValidator : AbstractValidator<EndImpersonationSessionCommand>
{
    public EndImpersonationSessionValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
