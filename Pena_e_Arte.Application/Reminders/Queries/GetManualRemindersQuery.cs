using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reminders.Queries;

/// <param name="QuickOnly">
/// "Quick reminders": raw-contact SMS sent to a typed-in name and phone, linked to no appointment and
/// no client, so neither of those filters can find them. Returns the newest <see cref="QuickReminderLimit"/>.
/// </param>
/// <param name="ArtistId">Optional filter for quick reminders for an owner/admin caller; an artist is
/// always scoped to their own.</param>
public record GetManualRemindersQuery(
    Guid? AppointmentId, Guid? ClientId, bool QuickOnly = false, Guid? ArtistId = null)
    : IRequest<List<ManualReminderResponse>>
{
    public const int QuickReminderLimit = 50;
}

public class GetManualRemindersHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetManualRemindersQuery, List<ManualReminderResponse>>
{
    public async Task<List<ManualReminderResponse>> Handle(GetManualRemindersQuery query, CancellationToken ct)
    {
        if (!query.QuickOnly && query.AppointmentId is null && query.ClientId is null)
            throw new BusinessRuleViolationException("Either appointmentId, clientId or quick=true is required.");
        if (query.QuickOnly && (query.AppointmentId is not null || query.ClientId is not null))
            throw new BusinessRuleViolationException("quick=true cannot be combined with appointmentId or clientId.");

        IQueryable<ManualReminder> q = db.ManualReminders.AsQueryable();
        if (query.AppointmentId is not null)
            q = q.Where(m => m.AppointmentId == query.AppointmentId);
        if (query.ClientId is not null)
            q = q.Where(m => m.ClientId == query.ClientId);
        if (query.QuickOnly)
        {
            q = q.Where(m => m.AppointmentId == null && m.ClientId == null);
            if (query.ArtistId is not null && currentUser.Role != "artist")
                q = q.Where(m => m.ArtistId == query.ArtistId);
        }

        // An artist only ever sees reminders they themselves set — never a colleague's,
        // even for the same appointment/client (matches CreateManualReminderHandler and
        // CancelManualReminderHandler's own ownership checks).
        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(m => m.ArtistId == myArtistId);
        }

        IQueryable<ManualReminder> ordered = q.OrderByDescending(m => m.ScheduledFor);
        // Quick reminders have no parent record to bound the list, so cap it; the others are naturally
        // bounded by their one appointment or client.
        if (query.QuickOnly)
            ordered = ordered.Take(GetManualRemindersQuery.QuickReminderLimit);

        return await ordered
            .Select(m => new ManualReminderResponse(
                m.Id, m.AppointmentId, m.ClientId, m.RecipientName, m.RecipientPhone, m.Message,
                m.ScheduledFor, m.Status.ToString(), m.SentAt, m.CreatedAt,
                m.FailureReason == null ? null : m.FailureReason.ToString()))
            .ToListAsync(ct);
    }
}
