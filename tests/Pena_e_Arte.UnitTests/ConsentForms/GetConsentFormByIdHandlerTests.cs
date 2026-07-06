using FluentAssertions;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.ConsentForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

/// <summary>Captures formatted log messages without NSubstitute's generic-method matching pitfalls on ILogger.Log&lt;TState&gt;.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel                          logLevel,
        EventId                            eventId,
        TState                             state,
        Exception?                         exception,
        Func<TState, Exception?, string>   formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

public class GetConsentFormByIdHandlerTests
{
    private readonly FakeDbContext                       _db       = FakeDbContext.Create();
    private readonly CapturingLogger<GetConsentFormByIdHandler> _logger = new();
    private readonly Guid                                _studioId = Guid.NewGuid();

    private GetConsentFormByIdHandler CreateSut(FakeCurrentUser? user = null) =>
        new(_db, user ?? FakeCurrentUser.Artist(), _logger);

    private async Task<Guid> SeedClient(Guid? userId = null)
    {
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = userId,
            FirstName = "Marco",
            LastName  = "Cliente",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId, Guid? artistId = null)
    {
        // Appointment.Artist is a required navigation — EF Core translates Include/ThenInclude
        // on it into an INNER JOIN, so a dangling ArtistId (as a real FK constraint would forbid)
        // would silently filter the whole ConsentForm out of the result. Always seed a real Artist.
        Guid resolvedArtistId = artistId ?? await SeedArtist();
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = resolvedArtistId,
            ClientId        = clientId,
            Date            = DateTime.UtcNow.AddDays(5),
            EndDate         = DateTime.UtcNow.AddDays(5).AddMinutes(60),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
            DepositAmount   = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task<Guid> SeedArtist()
    {
        Artist artist = new()
        {
            StudioId  = _studioId,
            FirstName = "Luca",
            LastName  = "Artista",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return artist.Id;
    }

    private async Task<Guid> SeedForm(Guid clientId, Guid appointmentId, DateTime? signedAt = null, DateTime? createdAt = null)
    {
        ConsentForm form = new()
        {
            StudioId      = _studioId,
            ClientId      = clientId,
            AppointmentId = appointmentId,
            SignatureData = "sig",
            SignedAt      = signedAt ?? DateTime.UtcNow,
            CreatedAt     = createdAt ?? DateTime.UtcNow,
        };
        _db.ConsentForms.Add(form);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return form.Id;
    }

    [Fact]
    public async Task Handle_ExistingId_ReturnsClientNameFromFirstAndLastName()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        Guid id            = await SeedForm(clientId, appointmentId);

        ConsentFormDetailResponse result = await CreateSut().Handle(new GetConsentFormByIdQuery(id), default);

        result.Id.Should().Be(id);
        result.StudioId.Should().Be(_studioId);
        result.ClientName.Should().Be("Marco Cliente");
    }

    [Fact]
    public async Task Handle_ClientLastNameEmpty_ReturnsTrimmedClientName()
    {
        Guid clientId = await SeedClient();
        _db.Clients.First(c => c.Id == clientId).LastName = "";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Guid appointmentId = await SeedAppointment(clientId);
        Guid id            = await SeedForm(clientId, appointmentId);

        ConsentFormDetailResponse result = await CreateSut().Handle(new GetConsentFormByIdQuery(id), default);

        result.ClientName.Should().Be("Marco");
    }

    [Fact]
    public async Task Handle_AppointmentHasArtist_ReturnsArtistNameAndId()
    {
        Guid clientId      = await SeedClient();
        Guid artistId      = await SeedArtist();
        Guid appointmentId = await SeedAppointment(clientId, artistId);
        Guid id            = await SeedForm(clientId, appointmentId);

        ConsentFormDetailResponse result = await CreateSut().Handle(new GetConsentFormByIdQuery(id), default);

        result.ArtistName.Should().Be("Luca Artista");
        result.ArtistId.Should().Be(artistId);
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetConsentFormByIdQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ClientRole_OwnForm_ReturnsForm()
    {
        Guid userId        = Guid.NewGuid();
        Guid clientId      = await SeedClient(userId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid id            = await SeedForm(clientId, appointmentId);

        FakeCurrentUser user = FakeCurrentUser.Client() with { UserId = userId };

        ConsentFormDetailResponse result = await CreateSut(user).Handle(new GetConsentFormByIdQuery(id), default);

        result.Id.Should().Be(id);
    }

    [Fact]
    public async Task Handle_ClientRole_AnotherClientsForm_ThrowsNotFoundException()
    {
        Guid ownerUserId   = Guid.NewGuid();
        await SeedClient(ownerUserId);
        Guid otherClientId = await SeedClient();
        Guid appointmentId = await SeedAppointment(otherClientId);
        Guid id            = await SeedForm(otherClientId, appointmentId);

        FakeCurrentUser user = FakeCurrentUser.Client() with { UserId = ownerUserId };

        Func<Task> act = () => CreateSut(user).Handle(new GetConsentFormByIdQuery(id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SignedAtBeforeCreatedAt_LogsWarning()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        DateTime createdAt = DateTime.UtcNow;
        DateTime signedAt  = createdAt.AddMinutes(-5);
        Guid id            = await SeedForm(clientId, appointmentId, signedAt, createdAt);

        await CreateSut().Handle(new GetConsentFormByIdQuery(id), default);

        _logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("before CreatedAt"));
    }
}
