using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetAppointmentIcsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetAppointmentIcsHandler CreateSut() => new(_db);

    private Guid SeedAppointment(bool includeArtist = true)
    {
        Artist? artist = includeArtist
            ? new Artist { StudioId = _studioId, FirstName = "Ink", LastName = "Master", Email = "ink@test.com" }
            : null;

        if (artist is not null) _db.Artists.Add(artist);

        Appointment appt = new()
        {
            StudioId        = _studioId,
            ArtistId        = artist?.Id ?? Guid.NewGuid(),
            ClientId        = Guid.NewGuid(),
            Date            = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
            EndDate         = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
            DepositAmount   = 50m,
        };
        _db.Appointments.Add(appt);
        if (artist is not null) appt.Artist = artist;
        _db.SaveChanges();
        return appt.Id;
    }

    [Fact]
    public async Task Handle_ValidAppointment_ReturnsIcsString()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().StartWith("BEGIN:VCALENDAR");
        ics.Should().Contain("END:VCALENDAR");
        ics.Should().Contain("BEGIN:VEVENT");
        ics.Should().Contain("END:VEVENT");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsCorrectDates()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DTSTART:20260915T100000Z");
        ics.Should().Contain("DTEND:20260915T120000Z");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsAppointmentUid()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain($"UID:{apptId}@pena-e-arte");
    }

    [Fact]
    public async Task Handle_ValidAppointment_ContainsDepositInDescription()
    {
        Guid apptId = SeedAppointment();

        string ics = await CreateSut().Handle(new GetAppointmentIcsQuery(apptId), default);

        ics.Should().Contain("DESCRIPTION:Deposit: 50.00 EUR");
    }

    [Fact]
    public async Task Handle_UnknownAppointment_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetAppointmentIcsQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
