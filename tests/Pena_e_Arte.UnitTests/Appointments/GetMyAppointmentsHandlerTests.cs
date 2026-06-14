using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetMyAppointmentsHandlerTests
{
    private readonly FakeDbContext _db           = FakeDbContext.Create();
    private readonly ICurrentUser  _currentUser  = Substitute.For<ICurrentUser>();
    private readonly Guid          _clientUserId = Guid.NewGuid();
    private readonly Guid          _studioId     = Guid.NewGuid();

    public GetMyAppointmentsHandlerTests() =>
        _currentUser.UserId.Returns(_clientUserId);

    private GetMyAppointmentsHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_ReturnsOnlyOwnAppointmentsOrderedByDate()
    {
        Guid ownClientId   = await SeedClient(_clientUserId);
        Guid otherClientId = await SeedClient(Guid.NewGuid());
        await SeedAppointment(ownClientId,   DateTime.UtcNow.AddDays(10));
        await SeedAppointment(ownClientId,   DateTime.UtcNow.AddDays(2));
        await SeedAppointment(otherClientId, DateTime.UtcNow.AddDays(5));

        List<AppointmentResponse> result = await CreateSut().Handle(new GetMyAppointmentsQuery(), default);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.ClientId == ownClientId);
        result.Should().BeInAscendingOrder(a => a.Date);
    }

    [Fact]
    public async Task Handle_NoAppointments_ReturnsEmptyList()
    {
        List<AppointmentResponse> result = await CreateSut().Handle(new GetMyAppointmentsQuery(), default);

        result.Should().BeEmpty();
    }

    private async Task<Guid> SeedClient(Guid userId)
    {
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = userId,
            FirstName = "Test",
            LastName  = "Client",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task SeedAppointment(Guid clientId, DateTime date)
    {
        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = clientId,
            Date            = date,
            EndDate         = date.AddMinutes(90),
            DurationMinutes = 90,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
