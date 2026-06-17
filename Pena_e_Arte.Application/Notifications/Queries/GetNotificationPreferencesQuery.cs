using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Notifications.Queries;

public record GetNotificationPreferencesQuery : IRequest<NotificationPreferencesResponse>;

public class GetNotificationPreferencesHandler(IAppDbContext db)
    : IRequestHandler<GetNotificationPreferencesQuery, NotificationPreferencesResponse>
{
    public async Task<NotificationPreferencesResponse> Handle(
        GetNotificationPreferencesQuery query, CancellationToken ct)
    {
        List<StudioNotificationPreference> existing = await db.StudioNotificationPreferences
            .AsNoTracking()
            .ToListAsync(ct);

        List<NotificationPreferenceItem> items = [];
        foreach (NotificationType type in Enum.GetValues<NotificationType>())
        foreach (NotificationChannel channel in Enum.GetValues<NotificationChannel>())
        {
            StudioNotificationPreference? pref = existing
                .FirstOrDefault(p => p.Type == type && p.Channel == channel);
            items.Add(new NotificationPreferenceItem(
                type.ToString(), channel.ToString(), pref?.IsEnabled ?? true));
        }

        return new NotificationPreferencesResponse(items);
    }
}
