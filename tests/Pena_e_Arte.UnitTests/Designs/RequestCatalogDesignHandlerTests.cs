using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class RequestCatalogDesignHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISlotLocker _locker = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public RequestCatalogDesignHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    private RequestCatalogDesignHandler CreateSut(FakeCurrentUser user) =>
        new(_db, _tenant, user, _locker, _jobs, _realtime, _sender, _planLimits);

    private Guid SeedCatalogDesignWithSchedule(out Guid artistId)
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Artists.Add(artist);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            _db.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
        }
        Design design = new() { StudioId = _studioId, ArtistId = artist.Id, ClientId = null, IsCatalogItem = true, Title = "Flash" };
        _db.Designs.Add(design);
        _db.SaveChanges();
        artistId = artist.Id;
        return design.Id;
    }

    [Fact]
    public async Task Handle_ClientRole_IgnoresRequestClientIdAndUsesJwtIdentity()
    {
        Guid catalogDesignId = SeedCatalogDesignWithSchedule(out Guid artistId);
        FakeCurrentUser clientUser = FakeCurrentUser.Client();
        Client myClient = new() { StudioId = _studioId, UserId = clientUser.UserId, FirstName = "Real", LastName = "Client", Email = "real@client.com" };
        _db.Clients.Add(myClient);
        _db.SaveChanges();

        Guid spoofedClientId = Guid.NewGuid();
        CreateAppointmentRequest booking = new(artistId, spoofedClientId, DateTime.UtcNow.AddDays(2), 60, null, "x");

        AppointmentResponse result = await CreateSut(clientUser).Handle(
            new RequestCatalogDesignCommand(catalogDesignId, booking), default);

        result.ClientId.Should().Be(myClient.Id);
        _db.Designs.Should().ContainSingle(d => d.ClientId == myClient.Id && d.Id != catalogDesignId);
    }

    [Fact]
    public async Task Handle_UnknownCatalogDesign_ThrowsNotFoundException()
    {
        CreateAppointmentRequest booking = new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(2), 60, null, "x");

        Func<Task> act = () => CreateSut(FakeCurrentUser.Owner()).Handle(
            new RequestCatalogDesignCommand(Guid.NewGuid(), booking), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
