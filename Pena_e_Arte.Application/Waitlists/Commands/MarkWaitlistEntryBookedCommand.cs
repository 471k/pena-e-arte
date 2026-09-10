using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Commands;

/// <summary>
/// Fired by the frontend right after a successful booking submission from the "Notify me" /
/// waitlist-claim flow — a thin, separate call rather than threading waitlist state through the
/// shared booking core (CreateAppointmentCoreAsync has no awareness of the waitlist).
/// </summary>
public record MarkWaitlistEntryBookedCommand(Guid WaitlistEntryId) : IRequest;

public class MarkWaitlistEntryBookedHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<MarkWaitlistEntryBookedCommand>
{
    public async Task Handle(MarkWaitlistEntryBookedCommand command, CancellationToken ct)
    {
        Domain.Entities.Waitlist entry = await db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == command.WaitlistEntryId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Waitlist), command.WaitlistEntryId);

        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != entry.ClientId)
                throw new NotFoundException(nameof(Domain.Entities.Waitlist), command.WaitlistEntryId);
        }

        if (entry.Status != WaitlistStatus.Notified)
            throw new BusinessRuleViolationException("Only a notified waitlist entry can be marked booked.");

        entry.Status = WaitlistStatus.Booked;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
