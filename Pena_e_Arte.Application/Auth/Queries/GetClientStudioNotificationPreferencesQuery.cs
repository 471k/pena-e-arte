using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Queries;

public record GetClientStudioNotificationPreferencesQuery(Guid StudioId)
    : IRequest<ClientNotificationPreferencesResponse>;

public class GetClientStudioNotificationPreferencesHandler(
    IAppDbContext db,
    ICurrentUser  currentUser)
    : IRequestHandler<GetClientStudioNotificationPreferencesQuery, ClientNotificationPreferencesResponse>
{
    // Only the notification types actually sent to clients — excludes owner-facing
    // types like IntakeFormSubmitted, ConsentFormSigned, DesignReviewed.
    private static readonly NotificationType[] ClientTypes =
    [
        NotificationType.AppointmentCreated,
        NotificationType.AppointmentConfirmed,
        NotificationType.AppointmentCancelled,
        NotificationType.DepositCaptured,
        NotificationType.PaymentRefunded,
    ];

    public async Task<ClientNotificationPreferencesResponse> Handle(
        GetClientStudioNotificationPreferencesQuery query, CancellationToken ct)
    {
        List<ClientNotificationPreference> saved = await db
            .ClientNotificationPreferences
            .Where(p => p.UserId == currentUser.UserId && p.StudioId == query.StudioId)
            .ToListAsync(ct);

        List<NotificationPreferenceItem> items = [];
        foreach (NotificationType type in ClientTypes)
        foreach (NotificationChannel channel in Enum.GetValues<NotificationChannel>())
        {
            bool isEnabled = saved
                .FirstOrDefault(p => p.Type == type && p.Channel == channel)
                ?.IsEnabled ?? true;

            items.Add(new NotificationPreferenceItem(type.ToString(), channel.ToString(), isEnabled));
        }

        return new ClientNotificationPreferencesResponse(items);
    }
}
