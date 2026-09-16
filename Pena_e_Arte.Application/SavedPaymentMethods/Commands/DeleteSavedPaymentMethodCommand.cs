using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.SavedPaymentMethods.Commands;

/// <summary>Removes one of the caller's own saved cards — local-only (no documented POK
/// "forget card" endpoint to also call; see IPokCardTokenService's doc comment for what POK's
/// Card Tokenization API does and doesn't expose).</summary>
public record DeleteSavedPaymentMethodCommand(Guid Id) : IRequest;

public class DeleteSavedPaymentMethodHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeleteSavedPaymentMethodCommand>
{
    public async Task Handle(DeleteSavedPaymentMethodCommand command, CancellationToken ct)
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        // 404-not-403 ownership pattern (matches CancelAppointmentHandler and friends) — a
        // saved method belonging to another client is reported the same as one that doesn't
        // exist at all.
        SavedPaymentMethod method = await db.SavedPaymentMethods
            .FirstOrDefaultAsync(s => s.Id == command.Id && s.ClientId == client.Id, ct)
            ?? throw new NotFoundException(nameof(SavedPaymentMethod), command.Id);

        method.DeletedAt = DateTime.UtcNow;

        if (method.IsDefault)
        {
            SavedPaymentMethod? nextDefault = await db.SavedPaymentMethods
                .Where(s => s.ClientId == client.Id && s.Id != method.Id)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (nextDefault is not null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
