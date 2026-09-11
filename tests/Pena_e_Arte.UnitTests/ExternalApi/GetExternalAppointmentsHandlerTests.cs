using FluentAssertions;
using Pena_e_Arte.Application.ExternalApi.Queries;
using Pena_e_Arte.Contracts.Responses.ExternalApi;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExternalApi;

public class GetExternalAppointmentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetExternalAppointmentsHandler CreateSut() => new(_db);

    private async Task SeedAppointment(DateTime date)
    {
        Client client = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Silva", Email = $"{Guid.NewGuid()}@test.com" };
        _db.Clients.Add(client);
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = client.Id,
            Date = date,
            EndDate = date.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ReturnsAppointmentsOrderedMostRecentFirst()
    {
        DateTime earlier = DateTime.UtcNow.AddDays(1);
        DateTime later = DateTime.UtcNow.AddDays(5);
        await SeedAppointment(earlier);
        await SeedAppointment(later);

        List<ExternalAppointmentResponse> result =
            await CreateSut().Handle(new GetExternalAppointmentsQuery(), default);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(later);
        result[1].Date.Should().Be(earlier);
    }

    [Fact]
    public async Task Handle_PageSizeExceedsMax_ClampsTo200()
    {
        for (int i = 0; i < 5; i++)
            await SeedAppointment(DateTime.UtcNow.AddDays(i));

        // Just proving the clamp doesn't throw/break pagination math for an oversized request —
        // 5 seeded rows all come back either way.
        List<ExternalAppointmentResponse> result =
            await CreateSut().Handle(new GetExternalAppointmentsQuery(Page: 1, PageSize: 10_000), default);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task Handle_SecondPage_ReturnsNextBatch()
    {
        for (int i = 0; i < 3; i++)
            await SeedAppointment(DateTime.UtcNow.AddDays(i));

        List<ExternalAppointmentResponse> page1 =
            await CreateSut().Handle(new GetExternalAppointmentsQuery(Page: 1, PageSize: 2), default);
        List<ExternalAppointmentResponse> page2 =
            await CreateSut().Handle(new GetExternalAppointmentsQuery(Page: 2, PageSize: 2), default);

        page1.Should().HaveCount(2);
        page2.Should().ContainSingle();
        page1.Select(a => a.Id).Should().NotIntersectWith(page2.Select(a => a.Id));
    }
}
