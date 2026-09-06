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
    // InApp notices (e.g. an admin generating a referral code) have no email/SMS
    // equivalent and aren't opted out of — only these two are toggleable.
    private static readonly NotificationChannel[] PreferenceChannels =
        [NotificationChannel.Email, NotificationChannel.Sms];

    public async Task<NotificationPreferencesResponse> Handle(
        GetNotificationPreferencesQuery query, CancellationToken ct)
    {
        List<StudioNotificationPreference> existing = await db.StudioNotificationPreferences
            .AsNoTracking()
            .ToListAsync(ct);

        List<NotificationPreferenceItem> items = [];
        foreach (NotificationType type in Enum.GetValues<NotificationType>())
            foreach (NotificationChannel channel in PreferenceChannels)
            {
                StudioNotificationPreference? pref = existing
                    .FirstOrDefault(p => p.Type == type && p.Channel == channel);
                items.Add(new NotificationPreferenceItem(
                    type.ToString(), channel.ToString(), pref?.IsEnabled ?? true));
            }

        return new NotificationPreferencesResponse(items);
    }
}
