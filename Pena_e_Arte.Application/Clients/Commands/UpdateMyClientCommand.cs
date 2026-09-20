using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

/// <summary>
/// Client self-service edit of their own name and phone. The target is resolved from
/// <see cref="ICurrentUser"/> — the command carries no id, so a client can only ever edit
/// themselves (IDOR-proof, same shape as <see cref="RequestMyDataErasureCommand"/>).
/// A person's name and phone are properties of the person, not of one studio relationship, and
/// <c>Client</c> is one row per studio (see docs/claude/architecture.md "Client identity model") —
/// so the edit fans out to every Client row sharing this UserId. Otherwise a client who fixes their
/// number while viewing studio A would still be texted at the old one by studio B.
/// </summary>
public record UpdateMyClientCommand(UpdateMyClientRequest Request) : IRequest<ClientResponse>, IAuditableCommand
{
    // Resolved by the handler; AuditLogBehavior reads the audit properties after it returns.
    public Guid ResolvedClientId { get; set; }

    // How many studios' Client rows were updated — never the ids, never the values (PII).
    public int AffectedClientCount { get; set; }

    public string AuditAction => AuditActions.ClientSelfProfileUpdated;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ResolvedClientId;
}

public class UpdateMyClientHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateMyClientCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(UpdateMyClientCommand command, CancellationToken ct)
    {
        Client client = await db.Clients
            .Include(c => c.Artist)
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        UpdateMyClientRequest req = command.Request;
        string firstName = req.FirstName.Trim();
        string lastName = req.LastName.Trim();
        string? phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();

        // `client` always has a UserId here (it was found by one), so the fan-out list always
        // contains it; the defensive add mirrors ClientDataErasure.ExecuteAsync.
        List<Client> allClients = await db.FindAllClientRecordsForUserAsync(client.UserId!.Value, ct);
        if (allClients.All(c => c.Id != client.Id))
            allClients.Add(client);

        DateTime now = DateTime.UtcNow;
        foreach (Client c in allClients)
        {
            c.FirstName = firstName;
            c.LastName = lastName;
            c.Phone = phone;
            c.UpdatedAt = now;
        }

        command.ResolvedClientId = client.Id;
        command.AffectedClientCount = allClients.Count;

        await db.SaveChangesAsync(ct);
        return CreateClientHandler.Map(client, client.Artist);
    }
}

public class UpdateMyClientValidator : AbstractValidator<UpdateMyClientCommand>
{
    public UpdateMyClientValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        // Blank means "remove my number" (handler stores null), so only a non-blank value has to
        // be well-formed — same E.164 rule as every other phone input (CreateClientValidator).
        RuleFor(x => x.Request.Phone)
            .MaximumLength(20)
            .Matches(PhoneValidationRules.E164Format)
            .WithMessage(PhoneValidationRules.E164ErrorMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Phone));
    }
}
