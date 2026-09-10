using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class RequestCatalogDesignHandlerIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private readonly ICurrentUser _user;
    private readonly ISlotLocker _locker;
    private readonly IJobScheduler _jobs;
    private readonly IRealtimeNotifier _realtime;
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    public RequestCatalogDesignHandlerIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _user = Substitute.For<ICurrentUser>();
        _locker = Substitute.For<ISlotLocker>();
        _jobs = Substitute.For<IJobScheduler>();
        _realtime = Substitute.For<IRealtimeNotifier>();

        _user.Role.Returns("artist");
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    [Fact]
    public async Task Handle_ValidCatalogItem_ClonesDesignAndRevisionAndAttachesReferenceImage()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid catalogDesignId = await SeedCatalogDesign(tenantId, artistId, "https://r2/flash-a.png");

        CreateAppointmentRequest booking = new(
            artistId, clientId, DateTime.UtcNow.AddDays(3), 90, null, "Booking the flash piece");
        AppointmentResponse result = await RunHandler(tenantId, catalogDesignId, booking);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        List<Design> designs = await verify.Designs.Where(d => d.ArtistId == artistId).ToListAsync();
        designs.Should().HaveCount(2); // catalog original + clone
        Design clone = designs.Single(d => d.Id != catalogDesignId);
        clone.ClientId.Should().Be(clientId);
        clone.IsCatalogItem.Should().BeFalse();

        List<DesignRevision> cloneRevisions = await verify.DesignRevisions
            .Where(r => r.DesignId == clone.Id).ToListAsync();
        cloneRevisions.Should().ContainSingle(r => r.FileUrl == "https://r2/flash-a.png" && r.VersionNumber == 1);

        Appointment appointment = await verify.Appointments.Include(a => a.Attachments)
            .SingleAsync(a => a.Id == result.Id);
        appointment.Attachments.Should().ContainSingle(a =>
            a.ImageUrl == "https://r2/flash-a.png" && a.Category == AppointmentAttachmentCategory.Reference);
    }

    [Fact]
    public async Task Handle_CatalogOriginalUnmodifiedAfterBooking()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        Guid catalogDesignId = await SeedCatalogDesign(tenantId, artistId, "https://r2/flash-b.png");

        CreateAppointmentRequest booking = new(
            artistId, clientId, DateTime.UtcNow.AddDays(3), 90, null, "Booking");
        await RunHandler(tenantId, catalogDesignId, booking);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        Design original = await verify.Designs.SingleAsync(d => d.Id == catalogDesignId);
        original.IsCatalogItem.Should().BeTrue();
        original.ClientId.Should().BeNull();
        original.Title.Should().Be("Flash Original");
    }

    [Fact]
    public async Task Handle_BookedTwiceByDifferentClients_ProducesTwoIndependentDesignsAndLeavesOriginalUntouched()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientA) = await SeedArtistAndClient(tenantId);
        (_, Guid clientB) = await SeedArtistAndClient(tenantId);
        Guid catalogDesignId = await SeedCatalogDesign(tenantId, artistId, "https://r2/flash-c.png");

        await RunHandler(tenantId, catalogDesignId,
            new(artistId, clientA, DateTime.UtcNow.AddDays(3), 60, null, "First booking"));
        await RunHandler(tenantId, catalogDesignId,
            new(artistId, clientB, DateTime.UtcNow.AddDays(4), 60, null, "Second booking"));

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        List<Design> clones = await verify.Designs
            .Where(d => d.ArtistId == artistId && d.Id != catalogDesignId).ToListAsync();

        clones.Should().HaveCount(2);
        clones.Select(d => d.Id).Distinct().Should().HaveCount(2);
        clones.Should().Contain(d => d.ClientId == clientA);
        clones.Should().Contain(d => d.ClientId == clientB);

        Design original = await verify.Designs.SingleAsync(d => d.Id == catalogDesignId);
        original.IsCatalogItem.Should().BeTrue();
        original.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonCatalogDesign_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        await using AppDbContext seedCtx = _fixture.CreateDbContext(tenantId);
        Design notCatalog = new() { StudioId = tenantId, ArtistId = artistId, ClientId = clientId, Title = "Regular design" };
        seedCtx.Designs.Add(notCatalog);
        await seedCtx.SaveChangesAsync();

        CreateAppointmentRequest booking = new(artistId, clientId, DateTime.UtcNow.AddDays(3), 60, null, "x");

        Func<Task> act = () => RunHandler(tenantId, notCatalog.Id, booking);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<(Guid ArtistId, Guid ClientId)> SeedArtistAndClient(Guid tenantId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        // StudioHours is seeded once per studio, not once per artist — see the identical
        // comment in AppointmentHandlerIntegrationTests.SeedArtistAndClient.
        bool studioHoursAlreadySeeded = await ctx.StudioHours.AnyAsync(h => h.StudioId == tenantId);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            ctx.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = tenantId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            if (!studioHoursAlreadySeeded)
            {
                ctx.StudioHours.Add(new StudioHours
                {
                    StudioId = tenantId,
                    DayOfWeek = day,
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                    IsOpen = true,
                });
            }
        }
        await ctx.SaveChangesAsync();

        return (artist.Id, client.Id);
    }

    private async Task<Guid> SeedCatalogDesign(Guid tenantId, Guid artistId, string fileUrl)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Design design = new()
        {
            StudioId = tenantId,
            ArtistId = artistId,
            ClientId = null,
            IsCatalogItem = true,
            Title = "Flash Original",
            Price = 120m,
        };
        ctx.Designs.Add(design);
        await ctx.SaveChangesAsync();

        ctx.DesignRevisions.Add(new DesignRevision
        {
            StudioId = tenantId,
            DesignId = design.Id,
            VersionNumber = 1,
            FileUrl = fileUrl,
        });
        await ctx.SaveChangesAsync();

        return design.Id;
    }

    private async Task<AppointmentResponse> RunHandler(Guid tenantId, Guid catalogDesignId, CreateAppointmentRequest booking)
    {
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        RequestCatalogDesignHandler handler = new(db, tenant, _user, _locker, _jobs, _realtime, _sender, _planLimits);
        return await handler.Handle(new RequestCatalogDesignCommand(catalogDesignId, booking), default);
    }
}
