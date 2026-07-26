using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetAppointmentHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingAppointment_ReturnsResponseWithClientName()
    {
        Client client = new() { FirstName = "Ana", LastName = "Costa", Email = "ana@test.com" };
        _db.Clients.Add(client);

        Appointment appt = new()
        {
            ArtistId = Guid.NewGuid(),
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();

        AppointmentResponse result = await CreateSut().Handle(new GetAppointmentQuery(appt.Id), default);

        result.Id.Should().Be(appt.Id);
        result.ClientName.Should().Be("Ana Costa");
    }

    [Fact]
    public async Task Handle_UnknownAppointment_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetAppointmentQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
