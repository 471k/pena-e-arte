using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class UpdateClientStudioNotificationPreferencesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Client();

    private UpdateClientStudioNotificationPreferencesHandler CreateSut() => new(_db, _identity, _currentUser);

    private void UserHasTenantIds(params Guid[] studioIds) =>
        _identity.GetTenantIdsAsync(_currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)studioIds);

    [Fact]
    public async Task Handle_UserNotMemberOfStudio_ThrowsNotFound()
    {
        Guid studioId = Guid.NewGuid();
        UserHasTenantIds(Guid.NewGuid()); // member of a different studio only
        List<NotificationPreferenceItem> preferences =
        [
            new(nameof(NotificationType.AppointmentCreated), nameof(NotificationChannel.Email), false),
        ];

        Func<Task> act = () => CreateSut().Handle(
            new UpdateClientStudioNotificationPreferencesCommand(studioId, preferences), default);

        await act.Should().ThrowAsync<NotFoundException>();
        _db.ClientNotificationPreferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NewPreference_CreatesRow()
    {
        Guid studioId = Guid.NewGuid();
        UserHasTenantIds(studioId);
        List<NotificationPreferenceItem> preferences =
        [
            new(nameof(NotificationType.AppointmentCreated), nameof(NotificationChannel.Email), false),
        ];

        await CreateSut().Handle(
            new UpdateClientStudioNotificationPreferencesCommand(studioId, preferences), default);

        ClientNotificationPreference created = _db.ClientNotificationPreferences.Single();
        created.UserId.Should().Be(_currentUser.UserId);
        created.StudioId.Should().Be(studioId);
        created.Type.Should().Be(NotificationType.AppointmentCreated);
        created.Channel.Should().Be(NotificationChannel.Email);
        created.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExistingPreference_UpdatesIsEnabled()
    {
        Guid studioId = Guid.NewGuid();
        UserHasTenantIds(studioId);
        _db.ClientNotificationPreferences.Add(new ClientNotificationPreference
        {
            UserId = _currentUser.UserId,
            StudioId = studioId,
            Type = NotificationType.DepositCaptured,
            Channel = NotificationChannel.Sms,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        List<NotificationPreferenceItem> preferences =
        [
            new(nameof(NotificationType.DepositCaptured), nameof(NotificationChannel.Sms), false),
        ];

        await CreateSut().Handle(
            new UpdateClientStudioNotificationPreferencesCommand(studioId, preferences), default);

        _db.ClientNotificationPreferences.Should().ContainSingle().Which.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TypeOutsideClientFacingSet_IsIgnored()
    {
        Guid studioId = Guid.NewGuid();
        UserHasTenantIds(studioId);
        List<NotificationPreferenceItem> preferences =
        [
            new(nameof(NotificationType.IntakeFormSubmitted), nameof(NotificationChannel.Email), false),
        ];

        await CreateSut().Handle(
            new UpdateClientStudioNotificationPreferencesCommand(studioId, preferences), default);

        _db.ClientNotificationPreferences.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PreferenceForOneStudio_DoesNotAffectAnotherStudio()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        UserHasTenantIds(studioA, studioB);
        _db.ClientNotificationPreferences.Add(new ClientNotificationPreference
        {
            UserId = _currentUser.UserId,
            StudioId = studioB,
            Type = NotificationType.AppointmentCreated,
            Channel = NotificationChannel.Email,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        List<NotificationPreferenceItem> preferences =
        [
            new(nameof(NotificationType.AppointmentCreated), nameof(NotificationChannel.Email), false),
        ];

        await CreateSut().Handle(
            new UpdateClientStudioNotificationPreferencesCommand(studioA, preferences), default);

        _db.ClientNotificationPreferences.Single(p => p.StudioId == studioB).IsEnabled.Should().BeTrue();
        _db.ClientNotificationPreferences.Single(p => p.StudioId == studioA).IsEnabled.Should().BeFalse();
    }
}
