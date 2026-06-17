using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class NotificationPreferenceService(IAppDbContext db) : INotificationPreferenceService
{
    public async Task<bool> IsEnabledAsync(
        Guid studioId, NotificationType type, NotificationChannel channel, CancellationToken ct)
    {
        StudioNotificationPreference? pref = await db.StudioNotificationPreferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.StudioId == studioId &&
                     p.Type == type &&
                     p.Channel == channel &&
                     p.DeletedAt == null,
                ct);

        return pref?.IsEnabled ?? true;
    }
}
