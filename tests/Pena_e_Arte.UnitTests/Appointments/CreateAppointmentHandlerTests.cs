using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CreateAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ISlotLocker _locker = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.Role.Returns("artist");
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    private CreateAppointmentHandler CreateSut() =>
        new(_db, _tenant, _user, _locker, _jobs, _realtime, _sender, _planLimits);

    [Fact]
    public void CreateAppointmentCommand_IsQuotaCheckedForAppointmentsPerMonth()
    {
        IQuotaCheckedCommand command = new CreateAppointmentCommand(ValidRequest());

        command.QuotaType.Should().Be(QuotaType.AppointmentsPerMonth);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAppointmentResponse()
    {
        CreateAppointmentRequest req = ValidRequest();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ArtistId.Should().Be(req.ArtistId);
        result.ClientId.Should().Be(req.ClientId);
        result.Date.Should().Be(req.Date);
        result.DurationMinutes.Should().Be(req.DurationMinutes);
        result.DepositAmount.Should().Be(0m);
        result.StudioId.Should().Be(_studioId);
        result.Status.Should().Be(AppointmentStatus.Pending.ToString());
    }

    [Fact]
    public async Task Handle_ActiveFixedDepositRule_UsesRuleAmount()
    {
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "Standard", AmountFixed = 75m, IsActive = true });
        await _db.SaveChangesAsync();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.DepositAmount.Should().Be(75m);
    }

    [Fact]
    public async Task Handle_NoActiveDepositRule_DepositAmountIsZero()
    {
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.DepositAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_InactiveDepositRule_DepositAmountIsZero()
    {
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "Standard", AmountFixed = 75m, IsActive = false });
        await _db.SaveChangesAsync();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.DepositAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsAppointmentToDb()
    {
        CreateAppointmentRequest req = ValidRequest();

        await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        _db.Appointments.Should().ContainSingle(a => a.ArtistId == req.ArtistId && a.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_ValidRequest_InvalidatesAppointmentsPerMonthUsageCache()
    {
        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await _planLimits.Received(1)
            .InvalidateUsageCacheAsync(QuotaType.AppointmentsPerMonth, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TimeOverlap_DoesNotInvalidateUsageCache()
    {
        CreateAppointmentRequest req = ValidRequest();
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = req.ArtistId,
            ClientId = Guid.NewGuid(),
            Date = req.Date.AddMinutes(30),
            EndDate = req.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();

        try { await CreateSut().Handle(new CreateAppointmentCommand(req), default); } catch { }

        await _planLimits.DidNotReceiveWithAnyArgs().InvalidateUsageCacheAsync(default, default);
    }

    [Fact]
    public async Task Handle_ValidRequest_SchedulesBothReminders()
    {
        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        _jobs.Received(1).ScheduleAppointmentReminder(Arg.Any<Guid>(), "48h", Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleAppointmentReminder(Arg.Any<Guid>(), "24h", Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_ValidRequest_NotifiesRealtimeWithAppointmentCreated()
    {
        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "AppointmentCreated", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LockNotAcquired_ThrowsSlotAlreadyBookedException()
    {
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(false);

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_TimeOverlap_ThrowsSlotAlreadyBookedException()
    {
        CreateAppointmentRequest req = ValidRequest();
        DateTime existingStart = req.Date.AddMinutes(30);
        DateTime existingEnd = req.Date.AddMinutes(90);

        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = req.ArtistId,
            ClientId = Guid.NewGuid(),
            Date = existingStart,
            EndDate = existingEnd,
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_CancelledOverlap_DoesNotThrow()
    {
        CreateAppointmentRequest req = ValidRequest();

        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = req.ArtistId,
            ClientId = Guid.NewGuid(),
            Date = req.Date.AddMinutes(30),
            EndDate = req.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status = AppointmentStatus.Cancelled,
            DepositStatus = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_TimeOverlap_StillReleasesLock()
    {
        CreateAppointmentRequest req = ValidRequest();

        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = req.ArtistId,
            ClientId = Guid.NewGuid(),
            Date = req.Date.AddMinutes(30),
            EndDate = req.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        try { await CreateSut().Handle(new CreateAppointmentCommand(req), default); } catch { }

        await _locker.Received(1)
            .ReleaseLockAsync(Arg.Any<Guid>(), req.ArtistId!.Value, req.Date, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientRole_IgnoresRequestClientIdAndUsesJwtIdentity()
    {
        Guid jwtUserId = Guid.NewGuid();
        Guid spoofedId = Guid.NewGuid();
        _user.Role.Returns("client");
        _user.UserId.Returns(jwtUserId);

        // The JWT carries the IdentityUser id; the handler must resolve the Client record
        Client ownClient = new()
        {
            StudioId = _studioId,
            UserId = jwtUserId,
            FirstName = "Jwt",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(ownClient);
        await _db.SaveChangesAsync();

        CreateAppointmentRequest req = ValidRequest() with { ClientId = spoofedId };
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ClientId.Should().Be(ownClient.Id);
        result.ClientId.Should().NotBe(spoofedId);
        result.ClientId.Should().NotBe(jwtUserId); // ClientId is the Client entity, not the user id
    }

    [Fact]
    public async Task Handle_ClientRoleWithoutClientRecord_ThrowsNotFoundException()
    {
        _user.Role.Returns("client");
        _user.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistRole_AllowsProvidedClientId()
    {
        Guid targetClientId = Guid.NewGuid();
        _user.Role.Returns("artist");

        CreateAppointmentRequest req = ValidRequest() with { ClientId = targetClientId };
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ClientId.Should().Be(targetClientId);
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        CreateAppointmentRequest req = new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PercentRuleWithArtistRate_ComputesDepositFromRateAndDuration()
    {
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "20%", AmountPercent = 20m, IsActive = true });
        await _db.SaveChangesAsync();

        // 90 min at €100/h = €150 estimated -> 20% = €30
        CreateAppointmentRequest req = ValidRequest(artistHourlyRate: 100m);
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.DepositAmount.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_PercentRuleWithoutArtistRate_DepositAmountIsZero()
    {
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "20%", AmountPercent = 20m, IsActive = true });
        await _db.SaveChangesAsync();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.DepositAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_MultipleActiveRules_LatestUpdatedRuleWins()
    {
        // Legacy data can violate the single-active invariant — selection must be deterministic
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId,
            Name = "Old",
            AmountFixed = 10m,
            IsActive = true,
            UpdatedAt = DateTime.UtcNow.AddDays(-10),
        });
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId,
            Name = "Newest",
            AmountFixed = 75m,
            IsActive = true,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.DepositAmount.Should().Be(75m);
    }

    [Fact]
    public async Task Handle_WithImageUrls_PersistsAttachmentsAndReturnsThemInOrder()
    {
        CreateAppointmentRequest req = ValidRequest() with
        {
            ImageUrls = ["https://cdn.example.com/1.png", "https://cdn.example.com/2.png"]
        };

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ImageUrls.Should().Equal("https://cdn.example.com/1.png", "https://cdn.example.com/2.png");
        _db.AppointmentAttachments.Should().HaveCount(2);
        _db.AppointmentAttachments.Should().OnlyContain(a => a.AppointmentId == result.Id && a.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_WithoutImageUrls_ReturnsEmptyImageUrls()
    {
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        result.ImageUrls.Should().BeEmpty();
        _db.AppointmentAttachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StudioChoiceBooking_ArtistAvailable_PersistsNullArtistId()
    {
        SeedArtist();
        CreateAppointmentRequest req = new(null, Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ArtistId.Should().BeNull();
        _db.Appointments.Should().ContainSingle(a => a.Id == result.Id && a.ArtistId == null);
    }

    [Fact]
    public async Task Handle_StudioChoiceBooking_PercentRule_DepositAmountIsZero()
    {
        SeedArtist(hourlyRate: 100m);
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "20%", AmountPercent = 20m, IsActive = true });
        await _db.SaveChangesAsync();

        CreateAppointmentRequest req = new(null, Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);
        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        // No specific artist rate to compute from at booking time — deferred to assignment.
        result.DepositAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_StudioChoiceBooking_NoActiveArtist_ThrowsBusinessRuleViolationException()
    {
        // No artist seeded at all — nothing can ever be available.
        CreateAppointmentRequest req = new(null, Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_StudioChoiceBooking_DoesNotAcquireSlotLock()
    {
        SeedArtist();
        CreateAppointmentRequest req = new(null, Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);

        await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await _locker.DidNotReceiveWithAnyArgs()
            .TryAcquireLockAsync(default, default, default, default);
    }

    private Guid SeedArtist(decimal? hourlyRate = null)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Seed",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@artist.test",
            HourlyRate = hourlyRate,
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();

        // Seed open schedule for all days so availability checks pass
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            _db.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
        }
        _db.SaveChanges();

        return artist.Id;
    }

    private CreateAppointmentRequest ValidRequest(decimal? artistHourlyRate = null) =>
        new(SeedArtist(artistHourlyRate), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, null);
}
