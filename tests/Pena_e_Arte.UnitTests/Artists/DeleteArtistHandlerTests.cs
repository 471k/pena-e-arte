using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class DeleteArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private DeleteArtistHandler CreateSut() => new(_db);

    private async Task<Artist> SeedArtist(string email)
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "A", LastName = "B", Email = email };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    [Fact]
    public async Task Handle_ExistingArtist_SetsDeletedAt()
    {
        Artist artist = await SeedArtist("rui@studio.com");

        await CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        _db.Artists.IgnoreQueryFilters()
            .Single(a => a.Id == artist.Id)
            .DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ExistingArtist_DoesNotHardDelete()
    {
        Artist artist = await SeedArtist("rui@studio.com");

        await CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        _db.Artists.IgnoreQueryFilters().Should().ContainSingle(a => a.Id == artist.Id);
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeleteArtistCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistWithUpcomingConfirmedAppointment_ThrowsBusinessRuleViolationException()
    {
        Artist artist = await SeedArtist("rui@studio.com");
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = artist.Id,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ArtistWithOnlyPastOrTerminalAppointments_Succeeds()
    {
        Artist artist = await SeedArtist("rui@studio.com");
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = artist.Id,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(-3),
            EndDate = DateTime.UtcNow.AddDays(-3).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        });
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = artist.Id,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Cancelled,
            DepositStatus = DepositStatus.Refunded,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ClientsAssignedToDeletedArtist_AreUnassigned()
    {
        Artist artist = await SeedArtist("rui@studio.com");
        Client client = new()
        {
            StudioId = _studioId,
            ArtistId = artist.Id,
            FirstName = "Ana",
            LastName = "Silva",
            Email = "ana@c.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new DeleteArtistCommand(artist.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArtistId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClientsAssignedToAnotherArtist_AreNotUnassigned()
    {
        Artist deletedArtist = await SeedArtist("rui@studio.com");
        Artist otherArtist = await SeedArtist("other@studio.com");
        Client client = new()
        {
            StudioId = _studioId,
            ArtistId = otherArtist.Id,
            FirstName = "Ana",
            LastName = "Silva",
            Email = "ana@c.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await CreateSut().Handle(new DeleteArtistCommand(deletedArtist.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArtistId.Should().Be(otherArtist.Id);
    }
}
