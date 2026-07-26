using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Queries;

public record GetClientStudioNotificationPreferencesQuery(Guid StudioId)
    : IRequest<ClientNotificationPreferencesResponse>;

public class GetClientStudioNotificationPreferencesHandler(
    IAppDbContext db,
    IIdentityService identity,
    ICurrentUser currentUser)
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

    // InApp notices (e.g. an issuer generating a referral code) have no email/SMS
    // equivalent and aren't opted out of — only these two are toggleable.
    private static readonly NotificationChannel[] PreferenceChannels =
        [NotificationChannel.Email, NotificationChannel.Sms];

    public async Task<ClientNotificationPreferencesResponse> Handle(
        GetClientStudioNotificationPreferencesQuery query, CancellationToken ct)
    {
        IReadOnlyList<Guid> tenantIds = await identity.GetTenantIdsAsync(currentUser.UserId, ct);
        if (!tenantIds.Contains(query.StudioId))
            throw new NotFoundException("Studio membership", query.StudioId);

        List<ClientNotificationPreference> saved = await db
            .ClientNotificationPreferences
            .Where(p => p.UserId == currentUser.UserId && p.StudioId == query.StudioId)
            .ToListAsync(ct);

        List<NotificationPreferenceItem> items = [];
        foreach (NotificationType type in ClientTypes)
            foreach (NotificationChannel channel in PreferenceChannels)
            {
                bool isEnabled = saved
                    .FirstOrDefault(p => p.Type == type && p.Channel == channel)
                    ?.IsEnabled ?? true;

                items.Add(new NotificationPreferenceItem(type.ToString(), channel.ToString(), isEnabled));
            }

        return new ClientNotificationPreferencesResponse(items);
    }
}
