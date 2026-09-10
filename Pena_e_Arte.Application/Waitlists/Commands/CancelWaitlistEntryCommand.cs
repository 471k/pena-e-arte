using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Commands;

// Mirrors CancelAppointmentCommand's own audit posture: both a client's self-cancel and a
// staff-initiated cancel are audited here, for the same consistency reason that command documents
// — a single command handles both paths and the actor role alone distinguishes them.
public record CancelWaitlistEntryCommand(Guid WaitlistEntryId) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.WaitlistEntryCancelled;
    public string AuditTargetType => AuditTargetTypes.WaitlistEntry;
    public Guid AuditTargetId => WaitlistEntryId;
}

public class CancelWaitlistEntryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CancelWaitlistEntryCommand>
{
    public async Task Handle(CancelWaitlistEntryCommand command, CancellationToken ct)
    {
        Domain.Entities.Waitlist entry = await db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == command.WaitlistEntryId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Waitlist), command.WaitlistEntryId);

        // 404 (not 403) on scope mismatch — matches CancelAppointmentHandler's convention so a
        // guessed entry id doesn't confirm a valid-but-not-theirs resource exists.
        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != entry.ClientId)
                throw new NotFoundException(nameof(Domain.Entities.Waitlist), command.WaitlistEntryId);
        }

        if (entry.Status is WaitlistStatus.Cancelled or WaitlistStatus.Booked or WaitlistStatus.Expired)
            return;

        entry.Status = WaitlistStatus.Cancelled;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
