using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class ResendArtistInviteHandlerTests
{
    private readonly FakeDbContext  _db        = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant    = Substitute.For<ICurrentTenant>();
    private readonly IJobScheduler  _scheduler = Substitute.For<IJobScheduler>();
    private readonly Guid           _studioId  = Guid.NewGuid();

    public ResendArtistInviteHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private ResendArtistInviteHandler CreateSut() => new(_db, _tenant, _scheduler);

    private async Task<Artist> SeedArtist(string email, string firstName = "Rui")
    {
        Artist artist = new() { StudioId = _studioId, FirstName = firstName, LastName = "Tavares", Email = email };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    [Fact]
    public async Task Handle_ExistingArtist_EnqueuesInviteForArtistEmail()
    {
        Artist artist = await SeedArtist("rui@studio.com");

        await CreateSut().Handle(new ResendArtistInviteCommand(artist.Id), default);

        _scheduler.Received(1).EnqueueArtistInvite("rui@studio.com", "Rui", _studioId);
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new ResendArtistInviteCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistNotFound_DoesNotEnqueueInvite()
    {
        try { await CreateSut().Handle(new ResendArtistInviteCommand(Guid.NewGuid()), default); } catch { }

        _scheduler.DidNotReceiveWithAnyArgs().EnqueueArtistInvite(default!, default!, default);
    }
}
