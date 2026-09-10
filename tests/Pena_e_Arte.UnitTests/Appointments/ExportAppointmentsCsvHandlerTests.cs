using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class ExportAppointmentsCsvHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ExportAppointmentsCsvHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private ExportAppointmentsCsvHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoAppointments_ReturnsHeaderRowOnly()
    {
        string csv = await CreateSut().Handle(new ExportAppointmentsCsvQuery(null, null), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        lines[0].Should().StartWith(CsvUtils.Bom + "Date,End Time,Duration (min),Artist,Client");
    }

    [Fact]
    public async Task Handle_OneAppointment_ProducesExpectedRow()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        await SeedAppointment(artistId, clientId, DateTime.UtcNow.AddDays(1));

        string csv = await CreateSut().Handle(new ExportAppointmentsCsvQuery(null, null), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("Luna Artista").And.Contain("Jane Doe").And.Contain("Pending");
    }

    [Fact]
    public async Task Handle_NameContainingCommaAndQuote_IsEscapedCorrectly()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane, \"JJ\"", "Doe");
        await SeedAppointment(artistId, clientId, DateTime.UtcNow.AddDays(1));

        string csv = await CreateSut().Handle(new ExportAppointmentsCsvQuery(null, null), default);

        csv.Should().Contain("\"Jane, \"\"JJ\"\" Doe\"");
    }

    [Fact]
    public async Task Handle_UnassignedAppointment_ShowsEmptyArtistColumn()
    {
        Guid clientId = await SeedClient("Jane", "Doe");
        await SeedAppointment(null, clientId, DateTime.UtcNow.AddDays(1));

        string csv = await CreateSut().Handle(new ExportAppointmentsCsvQuery(null, null), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().StartWith(DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Handle_FromToRange_FiltersByDate()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        await SeedAppointment(artistId, clientId, DateTime.UtcNow.AddMonths(-6));
        await SeedAppointment(artistId, clientId, DateTime.UtcNow.AddDays(1));

        string csv = await CreateSut().Handle(
            new ExportAppointmentsCsvQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(2)), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
    }

    private async Task<Guid> SeedArtist(string firstName, string lastName)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedClient(string firstName, string lastName)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client.Id;
    }

    private async Task SeedAppointment(Guid? artistId, Guid clientId, DateTime date)
    {
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = clientId,
            Date = date,
            EndDate = date.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
