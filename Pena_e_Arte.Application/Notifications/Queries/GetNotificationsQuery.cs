using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Notifications.Queries;

public record GetNotificationsQuery(
    Guid?     RecipientId,
    string?   Channel,
    DateTime? From,
    DateTime? To) : IRequest<List<NotificationLogResponse>>;

public class GetNotificationsHandler(IAppDbContext db)
    : IRequestHandler<GetNotificationsQuery, List<NotificationLogResponse>>
{
    public async Task<List<NotificationLogResponse>> Handle(
        GetNotificationsQuery query, CancellationToken ct)
    {
        IQueryable<NotificationLog> q = db.NotificationLogs.AsNoTracking();

        if (query.RecipientId.HasValue)
            q = q.Where(n => n.RecipientId == query.RecipientId.Value);

        if (query.Channel is not null)
            q = q.Where(n => n.Channel.ToString() == query.Channel);

        if (query.From.HasValue)
            q = q.Where(n => n.SentAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(n => n.SentAt <= query.To.Value);

        List<NotificationLog> logs = await q
            .OrderByDescending(n => n.SentAt)
            .ToListAsync(ct);

        return logs.Select(Map).ToList();
    }

    internal static NotificationLogResponse Map(NotificationLog n) => new(
        n.Id, n.RecipientId, n.Channel.ToString(),
        n.Subject, n.Body, n.SentAt, n.IsSuccess, n.CreatedAt);
}
