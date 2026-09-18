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
/// Non-destructive "remove client from list" — the ordinary Fresha/Vagaro-style "delete
/// client" that declutters the active roster while keeping every appointment/payment/
/// consent/review record fully intact and readable. Reversible via RestoreClientCommand.
/// Distinct from RequestDataErasureCommand, which destroys data permanently.
/// </summary>
public record ArchiveClientCommand(Guid ClientId) : IRequest<Unit>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientArchived;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class ArchiveClientHandler(IAppDbContext db)
    : IRequestHandler<ArchiveClientCommand, Unit>
{
    public async Task<Unit> Handle(ArchiveClientCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        if (client.ArchivedAt is not null) return Unit.Value; // idempotent

        client.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class ArchiveClientValidator : AbstractValidator<ArchiveClientCommand>
{
    public ArchiveClientValidator() => RuleFor(x => x.ClientId).NotEmpty();
}
