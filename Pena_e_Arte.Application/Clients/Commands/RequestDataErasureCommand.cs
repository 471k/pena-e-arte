using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

/// <summary>
/// Client right-to-erasure request (GDPR Art. 17). Soft-deletes the client's consent forms
/// and profile immediately, rather than waiting for the retention window — the two-stage
/// RetentionPurgeJob's hard-purge pass then permanently removes them after the grace window.
/// Audited with a DISTINCT action from the policy-driven automatic purge (which is not an
/// audited command), so an erasure requested by/for a data subject is always distinguishable
/// from routine retention housekeeping.
///
/// Invoked today by an owner/support action (OwnerOnly). No client-facing self-service UI
/// exists yet — that is a separate follow-up (open question §3.8), not silently absorbed here.
/// Tenant isolation: the global query filters scope every read/write to the caller's studio,
/// so an owner can only erase a client in their own studio.
/// </summary>
public record RequestDataErasureCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientDataErasureRequested;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class RequestDataErasureHandler(IAppDbContext db)
    : IRequestHandler<RequestDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(RequestDataErasureCommand command, CancellationToken ct)
    {
        Client client = await db.Clients
            .FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        DateTime now = DateTime.UtcNow;

        List<ConsentForm> forms = await db.ConsentForms
            .Where(f => f.ClientId == client.Id && f.DeletedAt == null)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms)
            form.DeletedAt = now;

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(p => p.ClientId == client.Id, ct);
        if (profile is not null)
            profile.DeletedAt = now;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class RequestDataErasureValidator : AbstractValidator<RequestDataErasureCommand>
{
    public RequestDataErasureValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
    }
}
