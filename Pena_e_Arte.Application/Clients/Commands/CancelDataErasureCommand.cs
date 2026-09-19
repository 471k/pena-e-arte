using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

/// <summary>
/// Support-mediated cancel of a pending erasure request during the retention grace window
/// (§Phase D). A client's own login is disabled immediately on erasure request, so they cannot
/// self-service this undo — an owner acts on their behalf, mirroring how RequestDataErasureCommand
/// already exists for the symmetric "client asked by phone" case.
/// </summary>
public record CancelDataErasureCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientDataErasureCancelled;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class CancelDataErasureHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<CancelDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(CancelDataErasureCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        if (client.ErasureRequestedAt is not DateTime requestedAt)
            throw new BusinessRuleViolationException("No pending erasure request for this client.");

        // Mirror Phase A's fan-out symmetrically — this is the same underlying data-subject
        // request being undone, not a per-studio one.
        List<Client> allClients = client.UserId is Guid uid
            ? await db.FindAllClientRecordsForUserAsync(uid, ct)
            : [client];
        List<Guid> clientIds = allClients.Select(c => c.Id).ToList();

        // Restore only the rows THIS erasure request soft-deleted — matched by the exact
        // ErasureRequestedAt timestamp stamped on each Client row in the same operation, so a
        // form/profile independently expired by the routine 7-year retention pass (unrelated to
        // this request) is never resurrected by mistake.
        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(f => clientIds.Contains(f.ClientId) && f.DeletedAt == requestedAt)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms) form.DeletedAt = null;

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => clientIds.Contains(p.ClientId) && p.DeletedAt == requestedAt)
            .ToListAsync(ct);
        foreach (ClientProfile profile in profiles) profile.DeletedAt = null;

        foreach (Client c in allClients) c.ErasureRequestedAt = null;

        await db.SaveChangesAsync(ct);

        if (client.UserId is Guid userId)
            await identity.EnableLoginAsync(userId, ct);

        return Unit.Value;
    }
}

public class CancelDataErasureValidator : AbstractValidator<CancelDataErasureCommand>
{
    public CancelDataErasureValidator() => RuleFor(x => x.ClientId).NotEmpty();
}
