using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Queries;
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
public class WaitlistHandlerIntegrationTests(DatabaseFixture fixture)
{
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IPaymentProvider _stripe = Substitute.For<IPaymentProvider>();

    [Fact]
    public async Task JoinWaitlist_AuthenticatedClient_CreatesEntryWithClientId()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId, userId);

        ICurrentUser clientUser = Substitute.For<ICurrentUser>();
        clientUser.IsAuthenticated.Returns(true);
        clientUser.Role.Returns("client");
        clientUser.UserId.Returns(userId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        JoinWaitlistHandler handler = new(db, TenantFor(tenantId), clientUser);

        WaitlistEntryResponse result = await handler.Handle(new JoinWaitlistCommand(
            new JoinWaitlistRequest(null, null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(5),
                null, null, null, null)), default);

        result.ClientId.Should().Be(clientId);
        result.StudioId.Should().Be(tenantId);
    }

    [Fact]
    public async Task JoinWaitlist_Guest_ResolvesStudioBySlugAndCreatesGuestEntry()
    {
        Guid tenantId = Guid.NewGuid();
        string slug = await SeedPublishedStudio(tenantId);

        ICurrentUser guest = Substitute.For<ICurrentUser>();
        guest.IsAuthenticated.Returns(false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        JoinWaitlistHandler handler = new(db, TenantFor(Guid.Empty), guest);

        WaitlistEntryResponse result = await handler.Handle(new JoinWaitlistCommand(
            new JoinWaitlistRequest(slug, null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(5),
                "Guest Name", "guest@example.com", "+355691234567", null)), default);

        result.StudioId.Should().Be(tenantId);
        result.GuestEmail.Should().Be("guest@example.com");
        result.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task CancelWaitlistEntry_OwnEntry_Succeeds()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId, userId);
        Guid entryId = await SeedWaitlistEntry(tenantId, clientId);

        ICurrentUser clientUser = Substitute.For<ICurrentUser>();
        clientUser.Role.Returns("client");
        clientUser.UserId.Returns(userId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CancelWaitlistEntryHandler handler = new(db, clientUser);
        await handler.Handle(new CancelWaitlistEntryCommand(entryId), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        Waitlist entry = await verify.WaitlistEntries.IgnoreQueryFilters().FirstAsync(w => w.Id == entryId);
        entry.Status.Should().Be(WaitlistStatus.Cancelled);
    }

    [Fact]
    public async Task CancelWaitlistEntry_AnotherClientsEntry_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        await SeedClient(tenantId, userId);
        Guid otherClientId = await SeedClient(tenantId, Guid.NewGuid());
        Guid entryId = await SeedWaitlistEntry(tenantId, otherClientId);

        ICurrentUser clientUser = Substitute.For<ICurrentUser>();
        clientUser.Role.Returns("client");
        clientUser.UserId.Returns(userId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CancelWaitlistEntryHandler handler = new(db, clientUser);

        Func<Task> act = () => handler.Handle(new CancelWaitlistEntryCommand(entryId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CancelAppointment_MatchingWaitlistEntry_NotifiesFirstInLine()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(10);
        Guid apptId = await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(90));
        Guid entryId = await SeedWaitlistEntry(
            tenantId, null, artistId, start.AddDays(-1), start.AddDays(1));

        ICurrentUser staffUser = Substitute.For<ICurrentUser>();
        staffUser.Role.Returns("owner");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CancelAppointmentHandler handler = new(db, TenantFor(tenantId), staffUser, _realtime, _sender, _jobs, _stripe);
        await handler.Handle(new CancelAppointmentCommand(apptId), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        Waitlist entry = await verify.WaitlistEntries.IgnoreQueryFilters().FirstAsync(w => w.Id == entryId);
        entry.Status.Should().Be(WaitlistStatus.Notified);
        entry.NotifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWaitlist_ArtistRole_SeesOnlyOwnArtistEntries()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistAId, _) = await SeedArtistAndClient(tenantId);
        (Guid artistBId, _) = await SeedArtistAndClient(tenantId);
        Guid userIdA = Guid.NewGuid();

        await using (AppDbContext setup = fixture.CreateDbContext(tenantId))
        {
            Artist a = await setup.Artists.FirstAsync(x => x.Id == artistAId);
            a.UserId = userIdA;
            await setup.SaveChangesAsync();
        }

        await SeedWaitlistEntry(tenantId, null, artistAId, DateTime.UtcNow, DateTime.UtcNow.AddDays(10));
        await SeedWaitlistEntry(tenantId, null, artistBId, DateTime.UtcNow, DateTime.UtcNow.AddDays(10));

        ICurrentUser artistUser = Substitute.For<ICurrentUser>();
        artistUser.Role.Returns("artist");
        artistUser.UserId.Returns(userIdA);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetWaitlistHandler handler = new(db, artistUser);
        List<WaitlistEntryResponse> result = await handler.Handle(new GetWaitlistQuery(), default);

        result.Should().OnlyContain(w => w.ArtistId == artistAId);
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedClient(Guid tenantId, Guid userId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "C",
            LastName = "L",
            Email = $"{Guid.NewGuid()}@c.com",
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return client.Id;
    }

    private async Task<string> SeedPublishedStudio(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        string slug = $"studio-{Guid.NewGuid():N}";
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Test Studio", Slug = slug, IsActive = true, IsPublished = true });
        await ctx.SaveChangesAsync();
        return slug;
    }

    private async Task<Guid> SeedWaitlistEntry(
        Guid tenantId, Guid? clientId, Guid? artistId = null, DateTime? from = null, DateTime? to = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Waitlist entry = new()
        {
            StudioId = tenantId,
            ClientId = clientId,
            ArtistId = artistId,
            GuestName = clientId is null ? "Guest" : null,
            GuestEmail = clientId is null ? "guest@example.com" : null,
            PreferredDateFrom = from ?? DateTime.UtcNow,
            PreferredDateTo = to ?? DateTime.UtcNow.AddDays(30),
            Status = WaitlistStatus.Waiting,
        };
        ctx.WaitlistEntries.Add(entry);
        await ctx.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<(Guid ArtistId, Guid ClientId)> SeedArtistAndClient(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

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
        }
        await ctx.SaveChangesAsync();

        return (artist.Id, client.Id);
    }

    private async Task<Guid> SeedAppointment(
        Guid tenantId, Guid artistId, Guid clientId, DateTime start, DateTime end)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Appointment appt = new()
        {
            StudioId = tenantId,
            ArtistId = artistId,
            ClientId = clientId,
            Date = start,
            EndDate = end,
            DurationMinutes = (int)(end - start).TotalMinutes,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();
        return appt.Id;
    }

    private static ICurrentTenant TenantFor(Guid tenantId)
    {
        CurrentTenantService t = new();
        t.SetTenant(tenantId);
        return t;
    }
}
