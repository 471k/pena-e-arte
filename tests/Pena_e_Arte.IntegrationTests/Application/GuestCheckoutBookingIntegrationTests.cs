using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Guest checkout has no tenant JWT — every query in the public/guest path runs against an
/// AppDbContext whose ICurrentTenant.StudioId is Guid.Empty (see CurrentTenantService). This
/// class exists specifically to catch the class of bug found during this feature's build: EF
/// Core's global query filter (StudioId == tenant.StudioId) still applies IN ADDITION TO any
/// explicit studioId predicate a handler adds, silently zeroing every result for an anonymous
/// caller unless IgnoreQueryFilters() is also called. Unit tests against FakeDbContext cannot
/// catch this — FakeDbContext never registers query filters at all — only a real,
/// filter-configured AppDbContext (this fixture) can.
/// </summary>
[Collection("Database")]
public class GuestCheckoutBookingIntegrationTests(DatabaseFixture fixture)
{
    private readonly ISlotLocker _locker = CreateAvailableLocker();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    private static ISlotLocker CreateAvailableLocker()
    {
        ISlotLocker locker = Substitute.For<ISlotLocker>();
        locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
              .Returns(true);
        return locker;
    }

    [Fact]
    public async Task CheckPublicSlotAvailability_SpecificArtistWithOpenSchedule_ReturnsAvailable()
    {
        (Studio studio, Guid artistId) = await SeedPublishedStudioWithArtist();
        DateTime slot = NextMondayAt(10);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        CheckPublicSlotAvailabilityHandler handler = new(db);
        SlotAvailabilityResult result = await handler.Handle(
            new CheckPublicSlotAvailabilityQuery(studio.Slug, artistId, slot, 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPublicSlotAvailability_AnyArtist_ActiveArtistAvailable_ReturnsAvailable()
    {
        (Studio studio, _) = await SeedPublishedStudioWithArtist();
        DateTime slot = NextMondayAt(10);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        CheckPublicSlotAvailabilityHandler handler = new(db);
        SlotAvailabilityResult result = await handler.Handle(
            new CheckPublicSlotAvailabilityQuery(studio.Slug, null, slot, 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicBookingArtists_ReturnsSeededArtist()
    {
        (Studio studio, Guid artistId) = await SeedPublishedStudioWithArtist();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetPublicBookingArtistsHandler handler = new(db);
        IReadOnlyList<PublicBookingArtistResponse> result =
            await handler.Handle(new GetPublicBookingArtistsQuery(studio.Slug), default);

        result.Should().ContainSingle(a => a.ArtistId == artistId);
    }

    [Fact]
    public async Task CreateGuestAppointment_ValidRequest_CreatesClientAppointmentAndBookingIntake()
    {
        (Studio studio, Guid artistId) = await SeedPublishedStudioWithArtist();
        DateTime slot = NextMondayAt(10);
        string email = $"guest-{Guid.NewGuid():N}@example.test";
        Guid newUserId = Guid.NewGuid();

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(email, Arg.Any<string>(), "client", studio.Id, "Jamie")
            .Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");
        _appSettings.BaseUrl.Returns("https://tattooos.co");
        _emailRenderer.RenderGuestBookingWelcome(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("<html></html>");

        CreateGuestAppointmentRequest request = new(
            "Jamie", "Guest", email, "+351912345678", MarketingOptIn: true,
            Booking: new CreateAppointmentRequest(
                artistId, Guid.Empty, slot, 60, "Anything else?", "A small rose",
                DesiredPlacementLocations: ["forearm_left"]));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        CreateGuestAppointmentHandler handler = new(
            db, _identity, _locker, _jobs, _realtime, _sender, _planLimits,
            _emailRenderer, _notifications, _appSettings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateGuestAppointmentHandler>.Instance);

        AppointmentResponse result = await handler.Handle(
            new CreateGuestAppointmentCommand(studio.Slug, request), default);

        result.ArtistId.Should().Be(artistId);
        result.TattooDescription.Should().Be("A small rose");
        result.DesiredPlacementLocations.Should().Equal("forearm_left");

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Client? client = verify.Clients.IgnoreQueryFilters().FirstOrDefault(c => c.Email == email);
        client.Should().NotBeNull();
        client!.UserId.Should().Be(newUserId);
        client.LastName.Should().Be("Guest");
        client.Phone.Should().Be("+351912345678");
        client.MarketingOptIn.Should().BeTrue();

        Appointment? appointment = verify.Appointments.IgnoreQueryFilters()
            .Include(a => a.Intake)
            .FirstOrDefault(a => a.Id == result.Id);
        appointment.Should().NotBeNull();
        appointment!.ClientId.Should().Be(client.Id);
        appointment.Intake.Should().NotBeNull();
        appointment.Intake!.TattooDescription.Should().Be("A small rose");
    }

    [Fact]
    public async Task CreateGuestAppointment_DuplicateEmail_ThrowsAccountAlreadyExistsException()
    {
        (Studio studio, Guid artistId) = await SeedPublishedStudioWithArtist();
        DateTime slot = NextMondayAt(10);
        string email = $"existing-{Guid.NewGuid():N}@example.test";

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        CreateGuestAppointmentRequest request = new(
            "Jamie", "Guest", email, "+351912345678", MarketingOptIn: false,
            Booking: new CreateAppointmentRequest(artistId, Guid.Empty, slot, 60, null, "A small rose"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        CreateGuestAppointmentHandler handler = new(
            db, _identity, _locker, _jobs, _realtime, _sender, _planLimits,
            _emailRenderer, _notifications, _appSettings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateGuestAppointmentHandler>.Instance);

        await FluentActions.Awaiting(() =>
            handler.Handle(new CreateGuestAppointmentCommand(studio.Slug, request), default))
            .Should().ThrowAsync<Pena_e_Arte.Domain.Exceptions.AccountAlreadyExistsException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Studio Studio, Guid ArtistId)> SeedPublishedStudioWithArtist()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Guest Checkout Studio",
            Slug = "guest-checkout-" + Guid.NewGuid().ToString("N")[..8],
            City = "Lisboa",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();

        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"artist-{Guid.NewGuid():N}@test.com",
            IsActive = true,
            HourlyRate = 100m,
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
        }
        await seed.SaveChangesAsync();

        return (studio, artist.Id);
    }

    private static DateTime NextMondayAt(int hour)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date.AddHours(hour);
    }
}
