using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reminders.Queries;

public record GetManualRemindersQuery(Guid? AppointmentId, Guid? ClientId)
    : IRequest<List<ManualReminderResponse>>;

public class GetManualRemindersHandler(IAppDbContext db)
    : IRequestHandler<GetManualRemindersQuery, List<ManualReminderResponse>>
{
    public async Task<List<ManualReminderResponse>> Handle(GetManualRemindersQuery query, CancellationToken ct)
    {
        if (query.AppointmentId is null && query.ClientId is null)
            throw new BusinessRuleViolationException("Either appointmentId or clientId is required.");

        IQueryable<ManualReminder> q = db.ManualReminders.AsQueryable();
        if (query.AppointmentId is not null)
            q = q.Where(m => m.AppointmentId == query.AppointmentId);
        if (query.ClientId is not null)
            q = q.Where(m => m.ClientId == query.ClientId);

        return await q
            .OrderByDescending(m => m.ScheduledFor)
            .Select(m => new ManualReminderResponse(
                m.Id, m.AppointmentId, m.ClientId, m.RecipientName, m.RecipientPhone, m.Message,
                m.ScheduledFor, m.Status.ToString(), m.SentAt, m.CreatedAt))
            .ToListAsync(ct);
    }
}
