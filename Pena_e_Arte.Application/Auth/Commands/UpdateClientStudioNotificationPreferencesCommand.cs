using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record UpdateClientStudioNotificationPreferencesCommand(
    Guid                                 StudioId,
    List<NotificationPreferenceItem>     Preferences)
    : IRequest<Unit>;

public class UpdateClientStudioNotificationPreferencesHandler(
    IAppDbContext    db,
    IIdentityService identity,
    ICurrentUser     currentUser)
    : IRequestHandler<UpdateClientStudioNotificationPreferencesCommand, Unit>
{
    private static readonly string[] ClientTypeNames =
    [
        nameof(NotificationType.AppointmentCreated),
        nameof(NotificationType.AppointmentConfirmed),
        nameof(NotificationType.AppointmentCancelled),
        nameof(NotificationType.DepositCaptured),
        nameof(NotificationType.PaymentRefunded),
    ];

    public async Task<Unit> Handle(
        UpdateClientStudioNotificationPreferencesCommand command, CancellationToken ct)
    {
        IReadOnlyList<Guid> tenantIds = await identity.GetTenantIdsAsync(currentUser.UserId, ct);
        if (!tenantIds.Contains(command.StudioId))
            throw new NotFoundException("Studio membership", command.StudioId);

        List<ClientNotificationPreference> existing = await db
            .ClientNotificationPreferences
            .Where(p => p.UserId == currentUser.UserId && p.StudioId == command.StudioId)
            .ToListAsync(ct);

        foreach (NotificationPreferenceItem item in command.Preferences)
        {
            // Only persist client-facing types — ignore anything outside the allowed set.
            if (!ClientTypeNames.Contains(item.Type)) continue;

            NotificationType    type    = Enum.Parse<NotificationType>(item.Type);
            NotificationChannel channel = Enum.Parse<NotificationChannel>(item.Channel);

            ClientNotificationPreference? pref = existing
                .FirstOrDefault(p => p.Type == type && p.Channel == channel);

            if (pref is null)
            {
                db.ClientNotificationPreferences.Add(new ClientNotificationPreference
                {
                    UserId    = currentUser.UserId,
                    StudioId  = command.StudioId,
                    Type      = type,
                    Channel   = channel,
                    IsEnabled = item.IsEnabled,
                });
            }
            else
            {
                pref.IsEnabled = item.IsEnabled;
            }
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class UpdateClientStudioNotificationPreferencesValidator
    : AbstractValidator<UpdateClientStudioNotificationPreferencesCommand>
{
    private static readonly string[] ValidTypes    = Enum.GetNames<NotificationType>();
    private static readonly string[] ValidChannels = Enum.GetNames<NotificationChannel>();

    public UpdateClientStudioNotificationPreferencesValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();

        RuleFor(x => x.Preferences)
            .NotNull()
            .Must(p => p.Count is > 0 and <= 64)
            .WithMessage("Preferences must contain between 1 and 64 items.");

        RuleForEach(x => x.Preferences).ChildRules(item =>
        {
            item.RuleFor(p => p.Type)
                .Must(t => ValidTypes.Contains(t))
                .WithMessage($"Type must be one of: {string.Join(", ", ValidTypes)}.");

            item.RuleFor(p => p.Channel)
                .Must(c => ValidChannels.Contains(c))
                .WithMessage($"Channel must be one of: {string.Join(", ", ValidChannels)}.");
        });
    }
}
