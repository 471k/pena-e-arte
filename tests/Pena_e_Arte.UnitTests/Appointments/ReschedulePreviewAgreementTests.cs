using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E04b (A02): the reschedule preview says exactly what the save will do about the status gate and the
/// notice window, for a client and for staff, because both call RescheduleEligibility.
/// </summary>
public class ReschedulePreviewAgreementTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    public ReschedulePreviewAgreementTests() => _tenant.StudioId.Returns(_studioId);

    public static TheoryData<string, AppointmentStatus, int> Cases()
    {
        TheoryData<string, AppointmentStatus, int> data = new();
        foreach (string role in new[] { "client", "artist", "owner" })
            foreach (AppointmentStatus status in Enum.GetValues<AppointmentStatus>())
                foreach (int hoursAway in new[] { 12, 24 * 10 }) // inside and outside the default 48 h window
                    data.Add(role, status, hoursAway);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ThePreview_PredictsTheSave(string role, AppointmentStatus status, int hoursAway)
    {
        _user.Role.Returns(role);
        _user.UserId.Returns(role == "client" ? _clientUserId : Guid.NewGuid());
        Guid id = await SeedAppointment(status, DateTime.UtcNow.AddHours(hoursAway));

        ReschedulePreviewResponse preview = await new GetReschedulePreviewHandler(_db, _user)
            .Handle(new GetReschedulePreviewQuery(id), default);

        Func<Task> save = () => new RescheduleAppointmentHandler(
                _db, _tenant, _user, Substitute.For<IRealtimeNotifier>(), Substitute.For<ISender>(), Substitute.For<IJobScheduler>())
            .Handle(new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(30), 60, null)), default);

        if (preview.CanReschedule)
        {
            await save.Should().NotThrowAsync();
        }
        else
        {
            (await save.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().Be(preview.Message);
        }

        // The rules themselves, stated: closed appointments never move; only a client is held to the window.
        bool closed = status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow;
        bool insideWindow = role == "client" && hoursAway < 48;
        preview.CanReschedule.Should().Be(!closed && !insideWindow);
        preview.NoticeWindowApplies.Should().Be(role == "client");
        preview.Block.Should().Be(closed ? "AppointmentClosed" : insideWindow ? "InsideNoticeWindow" : null);
    }

    [Fact]
    public async Task AClient_GetsNotFound_ForSomeoneElsesAppointment()
    {
        _user.Role.Returns("client");
        _user.UserId.Returns(Guid.NewGuid());
        _db.Clients.Add(new Client { StudioId = _studioId, UserId = _user.UserId, FirstName = "O", LastName = "T", Email = $"{Guid.NewGuid():N}@o.test" });
        await _db.SaveChangesAsync();
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed, DateTime.UtcNow.AddDays(10));

        Func<Task> act = () => new GetReschedulePreviewHandler(_db, _user).Handle(new GetReschedulePreviewQuery(id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task TheStudiosOwnWindow_IsTheOneApplied()
    {
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, IsActive = true, CancellationWindowHours = 72, UpdatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        _user.Role.Returns("client");
        _user.UserId.Returns(_clientUserId);
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed, DateTime.UtcNow.AddHours(60));

        ReschedulePreviewResponse preview = await new GetReschedulePreviewHandler(_db, _user)
            .Handle(new GetReschedulePreviewQuery(id), default);

        preview.CanReschedule.Should().BeFalse();
        preview.NoticeWindowHours.Should().Be(72);
        preview.Message.Should().Be("This appointment is less than 72 hours away — please contact the studio directly to reschedule.");
    }

    [Fact]
    public void TheDecision_IsPure_AndOrdersTheStatusGateFirst()
    {
        // A closed appointment inside the window reports the status, as the save always has.
        RescheduleEligibilityDecision d = RescheduleEligibility.Decide(
            AppointmentStatus.Completed, isClient: true, rule: null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow);

        d.Block.Should().Be(RescheduleBlock.AppointmentClosed);
        RescheduleEligibility.Message(d, AppointmentStatus.Completed).Should().Be("Cannot reschedule a Completed appointment.");
    }

    [Fact]
    public async Task TheValidator_RejectsAnEmptyId()
    {
        (await new GetReschedulePreviewValidator().ValidateAsync(new GetReschedulePreviewQuery(Guid.Empty)))
            .IsValid.Should().BeFalse();
    }

    private async Task<Guid> SeedAppointment(AppointmentStatus status, DateTime start)
    {
        Client client = await ClientForUser();
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task<Client> ClientForUser()
    {
        Client? existing = _db.Clients.FirstOrDefault(c => c.UserId == _clientUserId);
        if (existing is not null) return existing;
        Client client = new() { StudioId = _studioId, UserId = _clientUserId, FirstName = "Cli", LastName = "Ent", Email = $"{Guid.NewGuid():N}@c.test" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}
