using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Notifications.Commands;

public record UpdateNotificationPreferencesCommand(
    UpdateNotificationPreferencesRequest Request) : IRequest<Unit>;

public class UpdateNotificationPreferencesHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateNotificationPreferencesCommand, Unit>
{
    public async Task<Unit> Handle(UpdateNotificationPreferencesCommand command, CancellationToken ct)
    {
        Guid studioId = tenant.StudioId;

        List<StudioNotificationPreference> existing = await db.StudioNotificationPreferences
            .ToListAsync(ct);

        foreach (NotificationPreferenceItem item in command.Request.Preferences)
        {
            NotificationType    type    = Enum.Parse<NotificationType>(item.Type);
            NotificationChannel channel = Enum.Parse<NotificationChannel>(item.Channel);

            StudioNotificationPreference? pref = existing
                .FirstOrDefault(p => p.Type == type && p.Channel == channel);

            if (pref is null)
            {
                db.StudioNotificationPreferences.Add(new StudioNotificationPreference
                {
                    StudioId  = studioId,
                    Type      = type,
                    Channel   = channel,
                    IsEnabled = item.IsEnabled,
                });
            }
            else
            {
                pref.IsEnabled = item.IsEnabled;
                pref.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class UpdateNotificationPreferencesValidator
    : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    private static readonly string[] ValidTypes    = Enum.GetNames<NotificationType>();
    private static readonly string[] ValidChannels = Enum.GetNames<NotificationChannel>();

    public UpdateNotificationPreferencesValidator()
    {
        RuleFor(x => x.Request.Preferences)
            .NotNull()
            .Must(p => p.Count is > 0 and <= 64)
            .WithMessage("Preferences must contain between 1 and 64 items.");

        RuleForEach(x => x.Request.Preferences).ChildRules(item =>
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
