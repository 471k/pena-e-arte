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
/// Shared right-to-erasure logic (GDPR Art. 17): soft-deletes a client's consent forms and
/// profile immediately; the two-stage RetentionPurgeJob then permanently purges them after the
/// grace window. Both the owner/support command and the client self-service command below use
/// this, so the erasure behaviour is defined once.
/// </summary>
internal static class ClientDataErasure
{
    public static async Task SoftDeleteAsync(IAppDbContext db, Guid clientId, CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        List<ConsentForm> forms = await db.ConsentForms
            .Where(f => f.ClientId == clientId && f.DeletedAt == null)
            .ToListAsync(ct);
        foreach (ConsentForm form in forms)
            form.DeletedAt = now;

        ClientProfile? profile = await db.ClientProfiles
            .FirstOrDefaultAsync(p => p.ClientId == clientId, ct);
        if (profile is not null)
            profile.DeletedAt = now;

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Owner/support-initiated erasure of a specific client (OwnerOnly endpoint). Tenant query
/// filters scope it to the caller's own studio. Audited; the actor role recorded by
/// AuditLogBehavior ("owner"/"issuer") distinguishes this from a client's own self-service
/// request (actor role "client") below.
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

        await ClientDataErasure.SoftDeleteAsync(db, client.Id, ct);
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

    public string AuditAction => AuditActions.ClientDataErasureRequested;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ResolvedClientId;
}

public class RequestMyDataErasureHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RequestMyDataErasureCommand, Unit>
{
    public async Task<Unit> Handle(RequestMyDataErasureCommand command, CancellationToken ct)
    {
        // Resolve the caller's OWN client record — never an id from the request.
        Client client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        command.ResolvedClientId = client.Id;
        await ClientDataErasure.SoftDeleteAsync(db, client.Id, ct);
        return Unit.Value;
    }
}
