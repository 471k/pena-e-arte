using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

/// <summary>Reverses ArchiveClientCommand — restores a client to the active client list.</summary>
public record RestoreClientCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientRestored;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class RestoreClientHandler(IAppDbContext db)
    : IRequestHandler<RestoreClientCommand, Unit>
{
    public async Task<Unit> Handle(RestoreClientCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        if (client.ArchivedAt is null) return Unit.Value; // idempotent

        client.ArchivedAt = null;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class RestoreClientValidator : AbstractValidator<RestoreClientCommand>
{
    public RestoreClientValidator() => RuleFor(x => x.ClientId).NotEmpty();
}
