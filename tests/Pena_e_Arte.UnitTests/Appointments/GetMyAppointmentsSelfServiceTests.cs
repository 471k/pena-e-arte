using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// GET /appointments/mine carries the self-service policy per appointment, computed by the server, so
/// the bookings list never recomputes the notice window in the browser (G-3/G-17).
/// </summary>
public class GetMyAppointmentsSelfServiceTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetMyAppointmentsSelfServiceTests() => _currentUser.UserId.Returns(_userId);

    private Task<List<AppointmentResponse>> Mine() =>
        new GetMyAppointmentsHandler(_db, _currentUser).Handle(new GetMyAppointmentsQuery(), default);

    [Fact]
    public async Task NoRule_UsesThePlatformDefaultWindow_PerAppointment()
    {
        Guid clientId = await SeedClient();
        Guid far = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(5));
        Guid near = await SeedAppointment(clientId, DateTime.UtcNow.AddHours(30));

        List<AppointmentResponse> result = await Mine();

        result.Single(a => a.Id == far).SelfService.Should()
            .Be(new AppointmentSelfServiceResponse(AppointmentSelfServiceDefaults.CancellationWindowHours, true));
        result.Single(a => a.Id == near).SelfService.Should()
            .Be(new AppointmentSelfServiceResponse(AppointmentSelfServiceDefaults.CancellationWindowHours, false));
    }

    [Fact]
    public async Task ActiveRule_SetsTheWindow()
    {
        Guid clientId = await SeedClient();
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId,
            Name = "R",
            AmountFixed = 10m,
            IsActive = true,
            CancellationWindowHours = 24,
        });
        await _db.SaveChangesAsync();
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddHours(30));

        List<AppointmentResponse> result = await Mine();

        result.Single(a => a.Id == id).SelfService.Should().Be(new AppointmentSelfServiceResponse(24, true));
    }

    private async Task<Guid> SeedClient()
    {
        Client client = new() { StudioId = _studioId, UserId = _userId, FirstName = "T", LastName = "C", Email = "t@c.com" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = clientId,
            Date = date,
            EndDate = date.AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }
}
