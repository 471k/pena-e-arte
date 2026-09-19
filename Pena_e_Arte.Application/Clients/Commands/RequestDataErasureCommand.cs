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
/// Shared right-to-erasure logic (GDPR Art. 17). A person's "account" is not scoped to one
/// studio — Client is per-tenant but Identity login is shared across every studio they
/// belong to (see docs/claude/architecture.md's Decisions Log, "Client identity model",
/// 2026-09-18) — so erasure must act on every Client row for this UserId, not just the one
/// the caller happened to resolve from their active tenant. Immediately: soft-deletes the
/// consent forms and profile for EVERY studio Client row sharing this UserId, marks each row
/// for anonymization (ErasureRequestedAt), and disables the shared login once. The two-stage
/// RetentionPurgeJob then, after the grace window, physically removes the consent forms +
/// profiles and anonymizes every marked Client's PII + deletes the Identity user — its
/// existing cross-tenant sweep needs no changes to pick up more than one row.
/// </summary>
internal static class ClientDataErasure
{
    /// <summary>Returns the number of Client rows (across studios) this erasure touched.</summary>
    public static async Task<int> ExecuteAsync(
        IAppDbContext db, IIdentityService identity, Client client, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        // Fan out across every studio this person is a client at — not just `client` itself.
        // `client` (the caller's active-tenant row, or the specific row an owner targeted) is
        // always included: a Client with no UserId (never linked to a login, e.g. a walk-in the
        // studio pre-created) has nothing to fan out to, and the list will just contain itself.
        List<Client> allClients = client.UserId is Guid uid
            ? await db.FindAllClientRecordsForUserAsync(uid, ct)
            : [client];
        if (allClients.All(c => c.Id != client.Id))
            allClients.Add(client); // defensive — should be unreachable, `client` is always live

        List<Guid> clientIds = allClients.Select(c => c.Id).ToList();

        List<ConsentForm> forms = await db.ConsentForms
            .IgnoreQueryFilters()
            .Where(f => clientIds.Contains(f.ClientId) && f.DeletedAt == null)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms)
            form.DeletedAt = now;

        List<ClientProfile> profiles = await db.ClientProfiles
            .IgnoreQueryFilters()
            .Where(p => clientIds.Contains(p.ClientId))
            .ToListAsync(ct);
        foreach (ClientProfile profile in profiles)
            profile.DeletedAt = now;

        foreach (Client c in allClients)
            c.ErasureRequestedAt = now;

        await db.SaveChangesAsync(ct);

        // Disable login immediately — the user asked to delete their account and must not keep
        // signing in during the grace window.
        if (client.UserId is Guid userId)
            await identity.DisableLoginAsync(userId, ct);

        return allClients.Count;
    }
}

/// <summary>
/// Owner/support-initiated erasure of a specific client (OwnerOnly endpoint). Tenant query
/// filters scope it to the caller's own studio. Audited; the actor role recorded by
/// AuditLogBehavior ("owner"/"admin") distinguishes this from a client's own self-service
/// request (actor role "client") below.
/// </summary>
public record RequestDataErasureCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    // Set by the handler after ClientDataErasure.ExecuteAsync completes; AuditMetadataBuilder
    // reads it after the handler returns (§5.4 — how many studios' Client rows were touched,
    // never the ids themselves, never PII).
    public int AffectedClientCount { get; set; }

    public string AuditAction => AuditActions.ClientDataErasureRequested;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class RequestDataErasureHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<RequestDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(RequestDataErasureCommand command, CancellationToken ct)
    {
        Client client = await db.Clients
            .FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        command.AffectedClientCount = await ClientDataErasure.ExecuteAsync(db, identity, client, ct);
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

/// <summary>
/// Client-initiated self-service "delete my account" (GDPR Art. 17). The target client is
/// resolved from <see cref="ICurrentUser"/> — the command carries NO id, so a client can only
/// ever erase their own data (IDOR-proof: there is no id from the request to tamper with).
/// Audited with the same action as the owner command; AuditLogBehavior records actor role
/// "client", which distinguishes a self-service erasure from an owner/support-initiated one.
/// </summary>
public record RequestMyDataErasureCommand() : IRequest<Unit>, IAuditableCommand
{
    // Resolved by the handler from ICurrentUser before it returns; AuditLogBehavior reads
    // AuditTargetId after the handler completes.
    public Guid ResolvedClientId { get; set; }

    // Set by the handler after ClientDataErasure.ExecuteAsync completes (§5.4).
    public int AffectedClientCount { get; set; }

    public string AuditAction => AuditActions.ClientDataErasureRequested;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ResolvedClientId;
}

public class RequestMyDataErasureHandler(IAppDbContext db, ICurrentUser currentUser, IIdentityService identity)
    : IRequestHandler<RequestMyDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(RequestMyDataErasureCommand command, CancellationToken ct)
    {
        // Resolve the caller's OWN client record — never an id from the request.
        Client client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        command.ResolvedClientId = client.Id;
        command.AffectedClientCount = await ClientDataErasure.ExecuteAsync(db, identity, client, ct);
        return Unit.Value;
    }
}
