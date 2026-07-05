using FluentAssertions;
using Pena_e_Arte.Application.Auth.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class GetClientStudioNotificationPreferencesHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Client();

    private GetClientStudioNotificationPreferencesHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_NoSavedPreferences_ReturnsAllTypesDefaultingToEnabled()
    {
        Guid studioId = Guid.NewGuid();

        ClientNotificationPreferencesResponse result = await CreateSut().Handle(
            new GetClientStudioNotificationPreferencesQuery(studioId), default);

        result.Preferences.Should().HaveCount(10); // 5 client-facing types x 2 channels
        result.Preferences.Should().OnlyContain(p => p.IsEnabled);
    }

    [Fact]
    public async Task Handle_OnlyReturnsClientFacingTypes_ExcludesOwnerFacingTypes()
    {
        Guid studioId = Guid.NewGuid();

        ClientNotificationPreferencesResponse result = await CreateSut().Handle(
            new GetClientStudioNotificationPreferencesQuery(studioId), default);

        result.Preferences.Select(p => p.Type).Distinct().Should().BeEquivalentTo(
        [
            nameof(NotificationType.AppointmentCreated),
            nameof(NotificationType.AppointmentConfirmed),
            nameof(NotificationType.AppointmentCancelled),
            nameof(NotificationType.DepositCaptured),
            nameof(NotificationType.PaymentRefunded),
        ]);
    }

    [Fact]
    public async Task Handle_SavedPreferenceDisabled_ReflectsSavedValue()
    {
        Guid studioId = Guid.NewGuid();
        _db.ClientNotificationPreferences.Add(new ClientNotificationPreference
        {
            UserId    = _currentUser.UserId,
            StudioId  = studioId,
            Type      = NotificationType.AppointmentCreated,
            Channel   = NotificationChannel.Sms,
            IsEnabled = false,
        });
        await _db.SaveChangesAsync();

        ClientNotificationPreferencesResponse result = await CreateSut().Handle(
            new GetClientStudioNotificationPreferencesQuery(studioId), default);

        result.Preferences
            .Single(p => p.Type == nameof(NotificationType.AppointmentCreated) && p.Channel == nameof(NotificationChannel.Sms))
            .IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PreferenceSavedForDifferentStudio_DoesNotAffectResult()
    {
        Guid studioId      = Guid.NewGuid();
        Guid otherStudioId = Guid.NewGuid();
        _db.ClientNotificationPreferences.Add(new ClientNotificationPreference
        {
            UserId    = _currentUser.UserId,
            StudioId  = otherStudioId,
            Type      = NotificationType.AppointmentCreated,
            Channel   = NotificationChannel.Email,
            IsEnabled = false,
        });
        await _db.SaveChangesAsync();

        ClientNotificationPreferencesResponse result = await CreateSut().Handle(
            new GetClientStudioNotificationPreferencesQuery(studioId), default);

        result.Preferences
            .Single(p => p.Type == nameof(NotificationType.AppointmentCreated) && p.Channel == nameof(NotificationChannel.Email))
            .IsEnabled.Should().BeTrue();
    }
}
