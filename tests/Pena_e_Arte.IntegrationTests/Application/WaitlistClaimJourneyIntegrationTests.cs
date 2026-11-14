using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.Waitlists;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// C09 end to end on a real, filter-configured AppDbContext. The fake context used by the unit tests applies no
/// query filters, which is how the expiry job's cascade could pass its unit tests while matching nobody in
/// production (no tenant in a Hangfire job means the waitlist filter matches nothing). Every read here goes
/// through the same filters production does: anonymous claim reads and the job start with no tenant at all.
/// </summary>
[Collection("Database")]
public class WaitlistClaimJourneyIntegrationTests(DatabaseFixture fixture)
{
    private readonly WaitlistClaimTokens _tokens = new(new EphemeralDataProtectionProvider());
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly DateTime _slot = DateTime.UtcNow.Date.AddDays(4).AddHours(10);

    // ── The expiry job, with the real tenant filter ──────────────────────────

    [Fact]
    public async Task ExpiryJob_NoTenantLikeHangfire_StillCascadesTheOfferedSlot()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        await SeedNotifiedEntry(studio.Id, artistId, notifiedAt: DateTime.UtcNow.AddHours(-25), guestEmail: "late@example.com");
        Guid next = await SeedWaitingEntry(studio.Id, artistId, "next@example.com");

        await RunExpiryJob();

        Waitlist nextEntry = await Load(next);
        nextEntry.Status.Should().Be(WaitlistStatus.Notified);
        nextEntry.NotifiedSlotStart.Should().BeCloseTo(_slot, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExpiryJob_OfferedSlotBookedSince_ExpiresWithoutOfferingItToAnyone()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid late = await SeedNotifiedEntry(studio.Id, artistId, DateTime.UtcNow.AddHours(-25), guestEmail: "late@example.com");
        Guid next = await SeedWaitingEntry(studio.Id, artistId, "next@example.com");
        await SeedAppointment(studio.Id, artistId, _slot);

        await RunExpiryJob();

        (await Load(late)).Status.Should().Be(WaitlistStatus.Expired);
        (await Load(next)).Status.Should().Be(WaitlistStatus.Waiting);
    }

    // ── Guest journey: cancel → email link → claim page → guest booking → closed ──

    [Fact]
    public async Task GuestJourney_FromCancellationToBooking_ThenTheJobLeavesTheBookedEntryAlone()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid freedAppointment = await SeedAppointment(studio.Id, artistId, _slot);
        string guestEmail = $"guest-{Guid.NewGuid():N}@example.test";
        Guid entryId = await SeedWaitingEntry(studio.Id, artistId, guestEmail);

        // 1. The studio cancels: the first in line is offered this slot.
        await CancelAsStaff(studio.Id, freedAppointment);
        Waitlist offered = await Load(entryId);
        offered.Status.Should().Be(WaitlistStatus.Notified);
        offered.NotifiedSlotStart.Should().BeCloseTo(_slot, TimeSpan.FromSeconds(1));

        // 2. The email's token (minted from the row as the email handler does) opens the claim page, anonymously.
        string token = _tokens.Mint(offered);
        WaitlistClaimResponse claim = await ReadClaim(token);
        claim.State.Should().Be(WaitlistClaimStates.Claimable);
        claim.StudioSlug.Should().Be(studio.Slug);
        claim.ArtistId.Should().Be(artistId);

        // A second person joins the waitlist for the same day meanwhile.
        Guid second = await SeedWaitingEntry(studio.Id, artistId, "second@example.com");

        // 3. The guest books that slot with the token.
        await BookAsGuest(studio, artistId, guestEmail, _slot, token);

        (await Load(entryId)).Status.Should().Be(WaitlistStatus.Booked);
        (await ReadClaim(token)).State.Should().Be(WaitlistClaimStates.Booked);
        // They took the slot, so it isn't offered on.
        (await Load(second)).Status.Should().Be(WaitlistStatus.Waiting);

        // 4. A day later the job runs: the booked entry is neither expired nor cascaded.
        await AgeNotification(entryId, hours: 25);
        await RunExpiryJob();
        (await Load(entryId)).Status.Should().Be(WaitlistStatus.Booked);
        (await Load(second)).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task ForwardedLink_SomeoneElseBooks_TheOriginalPlaceStays()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid entryId = await SeedNotifiedEntry(studio.Id, artistId, DateTime.UtcNow.AddHours(-1), guestEmail: "owner-of-offer@example.com");
        string token = _tokens.Mint(await Load(entryId));

        await BookAsGuest(studio, artistId, $"stranger-{Guid.NewGuid():N}@example.test", _slot.AddHours(3), token);

        (await Load(entryId)).Status.Should().Be(WaitlistStatus.Notified);
    }

    // ── Signed-in client journey: "Book this slot" from My Waitlist, booked at another time ──

    [Fact]
    public async Task SignedInClient_BooksAnotherTime_EntryBooked_AndTheOfferedSlotMovesOn()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(studio.Id, userId);
        Guid entryId = await SeedNotifiedEntry(studio.Id, artistId, DateTime.UtcNow.AddHours(-1), clientId: clientId);
        Guid next = await SeedWaitingEntry(studio.Id, artistId, "next@example.com");
        ICurrentUser client = ClientUser(userId);

        // My Waitlist hands the client's own open offer the same token the email carries.
        List<WaitlistEntryResponse> mine;
        await using (AppDbContext db = fixture.CreateDbContext(studio.Id))
            mine = await new GetMyWaitlistEntriesHandler(db, client, _tokens).Handle(new GetMyWaitlistEntriesQuery(), default);
        string token = mine.Single(e => e.Id == entryId).ClaimToken!;
        (await ReadClaim(token)).State.Should().Be(WaitlistClaimStates.Claimable);

        await BookAsClient(studio.Id, client, clientId, artistId, _slot.AddHours(3), token);

        (await Load(entryId)).Status.Should().Be(WaitlistStatus.Booked);
        Waitlist nextEntry = await Load(next);
        nextEntry.Status.Should().Be(WaitlistStatus.Notified);
        nextEntry.NotifiedSlotStart.Should().BeCloseTo(_slot, TimeSpan.FromSeconds(1));
    }

    // ── Claim page states that must not look like a bug ──

    [Fact]
    public async Task ClaimAfterTheWindow_SaysTheWindowEnded()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid entryId = await SeedNotifiedEntry(studio.Id, artistId, DateTime.UtcNow.AddHours(-25), guestEmail: "late@example.com");

        (await ReadClaim(_tokens.Mint(await Load(entryId)))).State.Should().Be(WaitlistClaimStates.WindowEnded);
    }

    [Fact]
    public async Task SlotTakenMeanwhile_SaysSo()
    {
        (Studio studio, Guid artistId) = await SeedOpenStudioWithArtist();
        Guid entryId = await SeedNotifiedEntry(studio.Id, artistId, DateTime.UtcNow.AddHours(-1), guestEmail: "slow@example.com");
        await SeedAppointment(studio.Id, artistId, _slot);

        (await ReadClaim(_tokens.Mint(await Load(entryId)))).State.Should().Be(WaitlistClaimStates.SlotTaken);
    }

    // ── Steps ────────────────────────────────────────────────────────────────

    private async Task<WaitlistClaimResponse> ReadClaim(string token)
    {
        CurrentTenantService anonymous = new();
        await using AppDbContext db = fixture.CreateDbContext(anonymous);
        return await new GetWaitlistClaimHandler(db, anonymous, _tokens, NullLogger<GetWaitlistClaimHandler>.Instance)
            .Handle(new GetWaitlistClaimQuery(token), default);
    }

    private async Task RunExpiryJob()
    {
        CurrentTenantService none = new();
        await using AppDbContext db = fixture.CreateDbContext(none);
        await new WaitlistNotificationExpiryJob(db, none, Substitute.For<ISender>(),
            NullLogger<WaitlistNotificationExpiryJob>.Instance).RunAsync();
    }

    private async Task CancelAsStaff(Guid studioId, Guid appointmentId)
    {
        ICurrentUser staff = Substitute.For<ICurrentUser>();
        staff.Role.Returns("owner");
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        await using AppDbContext db = fixture.CreateDbContext(tenant);
        await new CancelAppointmentHandler(db, tenant, staff, _realtime, Substitute.For<ISender>(), _jobs, _payments,
            NullLogger<CancelAppointmentHandler>.Instance).Handle(new CancelAppointmentCommand(appointmentId), default);
    }

    private async Task BookAsGuest(Studio studio, Guid artistId, string email, DateTime start, string token)
    {
        IIdentityService identity = Substitute.For<IIdentityService>();
        Guid newUserId = Guid.NewGuid();
        identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        identity.CreateUserAsync(email, Arg.Any<string>(), "client", studio.Id, "Jamie")
            .Returns((true, newUserId, Array.Empty<string>()));
        identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");
        IAppSettings settings = Substitute.For<IAppSettings>();
        settings.BaseUrl.Returns("https://tattooos.co");
        IEmailRenderer renderer = Substitute.For<IEmailRenderer>();
        renderer.RenderGuestBookingWelcome(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("<html></html>");

        CurrentTenantService tenant = new();
        await using AppDbContext db = fixture.CreateDbContext(tenant);
        CreateGuestAppointmentHandler handler = new(
            db, tenant, identity, Locker(), _jobs, _realtime, RoutingSender(db), _planLimits,
            renderer, Substitute.For<INotificationService>(), settings,
            NullLogger<CreateGuestAppointmentHandler>.Instance);

        await handler.Handle(new CreateGuestAppointmentCommand(studio.Slug, new CreateGuestAppointmentRequest(
            "Jamie", "Guest", email, "+351912345678", MarketingOptIn: false,
            Booking: new CreateAppointmentRequest(artistId, Guid.Empty, start, 60, null, "A small rose",
                WaitlistClaimToken: token))), default);
    }

    private async Task BookAsClient(Guid studioId, ICurrentUser client, Guid clientId, Guid artistId, DateTime start, string token)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        await using AppDbContext db = fixture.CreateDbContext(tenant);
        CreateAppointmentHandler handler = new(db, tenant, client, Locker(), _jobs, _realtime, RoutingSender(db), _planLimits);

        await handler.Handle(new CreateAppointmentCommand(new CreateAppointmentRequest(
            artistId, clientId, start, 60, null, "A small rose", WaitlistClaimToken: token)), default);
    }

    /// <summary>Runs the real CompleteWaitlistClaimHandler on the booking's own context, as MediatR would.</summary>
    private ISender RoutingSender(AppDbContext db)
    {
        ISender sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CompleteWaitlistClaimCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => new CompleteWaitlistClaimHandler(db, _tokens, Substitute.For<ISender>(),
                    NullLogger<CompleteWaitlistClaimHandler>.Instance)
                .Handle(ci.Arg<CompleteWaitlistClaimCommand>(), ci.Arg<CancellationToken>()));
        return sender;
    }

    private static ISlotLocker Locker()
    {
        ISlotLocker locker = Substitute.For<ISlotLocker>();
        locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return locker;
    }

    private static ICurrentUser ClientUser(Guid userId)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Role.Returns("client");
        user.UserId.Returns(userId);
        return user;
    }

    // ── Seeding ──────────────────────────────────────────────────────────────

    private async Task<Waitlist> Load(Guid entryId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        return await db.WaitlistEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(w => w.Id == entryId);
    }

    private async Task AgeNotification(Guid entryId, int hours)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Waitlist entry = await db.WaitlistEntries.IgnoreQueryFilters().SingleAsync(w => w.Id == entryId);
        entry.NotifiedAt = entry.NotifiedAt!.Value.AddHours(-hours);
        await db.SaveChangesAsync();
    }

    private async Task<(Studio Studio, Guid ArtistId)> SeedOpenStudioWithArtist()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Waitlist Studio",
            Slug = "waitlist-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            OwnerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            Timezone = "UTC",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"artist-{Guid.NewGuid():N}@test.com",
            IsActive = true,
        };
        seed.Artists.Add(artist);
        await seed.SaveChangesAsync();

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            seed.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = studio.Id,
                ArtistId = artist.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            seed.StudioHours.Add(new StudioHours
            {
                StudioId = studio.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsOpen = true,
            });
        }
        await seed.SaveChangesAsync();
        return (studio, artist.Id);
    }

    private async Task<Guid> SeedClient(Guid studioId, Guid userId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId = studioId,
            UserId = userId,
            FirstName = "Ana",
            LastName = "K",
            Email = $"{Guid.NewGuid():N}@client.test",
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid studioId, Guid artistId, DateTime start)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Client client = new() { StudioId = studioId, FirstName = "Other", LastName = "Client", Email = $"{Guid.NewGuid():N}@client.test" };
        db.Clients.Add(client);
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddMinutes(60),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Guid> SeedWaitingEntry(Guid studioId, Guid artistId, string guestEmail)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Waitlist entry = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            GuestName = "Guest",
            GuestEmail = guestEmail,
            PreferredDateFrom = _slot.AddDays(-1),
            PreferredDateTo = _slot.AddDays(1),
            Status = WaitlistStatus.Waiting,
        };
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedNotifiedEntry(
        Guid studioId, Guid artistId, DateTime notifiedAt, string? guestEmail = null, Guid? clientId = null)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Waitlist entry = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = clientId,
            GuestName = guestEmail is null ? null : "Guest",
            GuestEmail = guestEmail,
            PreferredDateFrom = _slot.AddDays(-1),
            PreferredDateTo = _slot.AddDays(1),
            Status = WaitlistStatus.Notified,
            NotifiedAt = new DateTime(notifiedAt.Ticks - (notifiedAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            NotifiedSlotStart = _slot,
            NotifiedArtistId = artistId,
            NotifiedSlotDurationMinutes = 60,
        };
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }
}
