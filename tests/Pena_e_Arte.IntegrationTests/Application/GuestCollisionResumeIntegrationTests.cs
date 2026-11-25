using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.BookingDrafts.Commands;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.BookingDrafts;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// C05 on the real, filter-configured context: the collision path writes an unclaimed draft its owner cannot see,
/// the resume link plus the owner's sign-in makes it visible, nobody else can claim it, and the guest endpoint's
/// response stays byte-identical whether or not the email has an account.
/// </summary>
[Collection("Database")]
public class GuestCollisionResumeIntegrationTests
{
    private readonly DatabaseFixture _fixture;
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly BookingDraftResumeTokens _tokens = new(new EphemeralDataProtectionProvider());

    // The API's own serializer settings for minimal-API results (ASP.NET Core's web defaults).
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);

    private sealed record Caller(Guid UserId) : ICurrentUser
    {
        public string Role => "client";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
    }

    public GuestCollisionResumeIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _appSettings.BaseUrl.Returns("https://app.tattooos.co");
        _emailRenderer.RenderGuestBookingEmailCollision(Arg.Any<string>(), Arg.Any<string?>()).Returns("<html></html>");
        _emailRenderer.RenderGuestBookingWelcome(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("<html></html>");
    }

    private ISlotLocker Locker()
    {
        ISlotLocker locker = Substitute.For<ISlotLocker>();
        locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        return locker;
    }

    private async Task<GuestBookingAckResponse> Book(Studio studio, CreateGuestAppointmentRequest request)
    {
        CurrentTenantService tenant = new();
        await using AppDbContext db = _fixture.CreateDbContext(tenant);
        CreateGuestAppointmentHandler handler = new(
            db, tenant, _identity, Locker(), Substitute.For<IJobScheduler>(), Substitute.For<IRealtimeNotifier>(),
            Substitute.For<ISender>(), Substitute.For<IPlanLimitService>(), _emailRenderer, _notifications, _appSettings,
            _tokens, TimeProvider.System, NullLogger<CreateGuestAppointmentHandler>.Instance);
        return await handler.Handle(new CreateGuestAppointmentCommand(studio.Slug, request), default);
    }

    private static CreateGuestAppointmentRequest Request(string email, Guid artistId) => new(
        "Jamie", "Guest", email, "+355691234567", MarketingOptIn: false,
        Booking: new CreateAppointmentRequest(
            artistId, Guid.Empty, NextMondayAt(10), 60, null, "A swallow on the wrist",
            SafetyNotes: "On blood thinners"));

    private Guid ExistingClientAccount(string email)
    {
        Guid userId = Guid.NewGuid();
        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(userId);
        _identity.GetUserRolesAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<string> { "client" });
        return userId;
    }

    private string MailedToken()
    {
        string url = (string)_emailRenderer.ReceivedCalls()
            .Last(c => c.GetMethodInfo().Name == nameof(IEmailRenderer.RenderGuestBookingEmailCollision))
            .GetArguments()[1]!;
        return url[(url.IndexOf("#t=", StringComparison.Ordinal) + 3)..];
    }

    private async Task<BookingDraftResponse?> Mine(Guid studioId, Guid userId)
    {
        await using AppDbContext db = _fixture.CreateDbContext(studioId);
        return await new GetMyBookingDraftHandler(db, new Caller(userId), TimeProvider.System).Handle(new GetMyBookingDraftQuery(), default);
    }

    private async Task<ResumeBookingDraftResponse> Resume(Guid studioId, Guid userId, string token, TimeProvider? time = null)
    {
        await using AppDbContext db = _fixture.CreateDbContext(studioId);
        return await new ResumeBookingDraftHandler(db, new Caller(userId), _tokens, time ?? TimeProvider.System)
            .Handle(new ResumeBookingDraftCommand(token), default);
    }

    [Fact]
    public async Task Collision_WritesAnUnclaimedDraft_InvisibleToItsOwner_UntilTheLinkAndSignInBothProveIt()
    {
        (Studio studio, Guid artistId) = await SeedStudio();
        string email = $"has-account-{Guid.NewGuid():N}@example.test";
        Guid ownerId = ExistingClientAccount(email);

        await Book(studio, Request(email, artistId));

        await using (AppDbContext raw = _fixture.CreateDbContext(studio.Id))
        {
            BookingDraft row = await raw.BookingDrafts.SingleAsync(d => d.UserId == ownerId);
            row.ClaimedAt.Should().BeNull();
            row.PayloadJson.Should().NotContainEquivalentOf("safetyNotes").And.NotContain("blood thinners");
        }
        (await Mine(studio.Id, ownerId)).Should().BeNull("an unclaimed draft is not attached to the account");

        ResumeBookingDraftResponse resumed = await Resume(studio.Id, ownerId, MailedToken());

        resumed.Outcome.Should().Be(ResumeBookingDraftHandler.Resumed);
        BookingDraftResponse? mine = await Mine(studio.Id, ownerId);
        mine.Should().NotBeNull();
        mine!.Payload.Should().Contain("A swallow on the wrist");
    }

    [Fact]
    public async Task ASecondCollision_ReplacesTheUnclaimedDraft_UnderTheUniqueIndex_AndOnlyTheNewLinkWorks()
    {
        // Delete-then-insert of the same (StudioId, UserId) in one SaveChanges: only real MySQL enforces the index.
        (Studio studio, Guid artistId) = await SeedStudio();
        string email = $"has-account-{Guid.NewGuid():N}@example.test";
        Guid ownerId = ExistingClientAccount(email);
        await Book(studio, Request(email, artistId));
        string firstToken = MailedToken();
        await Task.Delay(1100); // a different whole-second CreatedAt, as two real attempts would have

        CreateGuestAppointmentRequest second = Request(email, artistId);
        await Book(studio, second with { Booking = second.Booking with { TattooDescription = "A second try" } });
        string secondToken = MailedToken();

        Func<Task> oldLink = () => Resume(studio.Id, ownerId, firstToken);
        await oldLink.Should().ThrowAsync<NotFoundException>();
        ResumeBookingDraftResponse resumed = await Resume(studio.Id, ownerId, secondToken);
        resumed.Draft!.Payload.Should().Contain("A second try");
        await using AppDbContext raw = _fixture.CreateDbContext(studio.Id);
        (await raw.BookingDrafts.CountAsync(d => d.UserId == ownerId)).Should().Be(1);
    }

    [Fact]
    public async Task Resume_ByADifferentSignedInUser_IsRefused_AndClaimsNothing()
    {
        (Studio studio, Guid artistId) = await SeedStudio();
        string email = $"has-account-{Guid.NewGuid():N}@example.test";
        Guid ownerId = ExistingClientAccount(email);
        await Book(studio, Request(email, artistId));
        string token = MailedToken();

        Func<Task> stranger = () => Resume(studio.Id, Guid.NewGuid(), token);

        await stranger.Should().ThrowAsync<NotFoundException>();
        await using AppDbContext raw = _fixture.CreateDbContext(studio.Id);
        (await raw.BookingDrafts.SingleAsync(d => d.UserId == ownerId)).ClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task Resume_SignedInToAnotherStudio_IsRefused()
    {
        (Studio studio, Guid artistId) = await SeedStudio();
        string email = $"has-account-{Guid.NewGuid():N}@example.test";
        Guid ownerId = ExistingClientAccount(email);
        await Book(studio, Request(email, artistId));

        Func<Task> elsewhere = () => Resume(Guid.NewGuid(), ownerId, MailedToken());

        await elsewhere.Should().ThrowAsync<NotFoundException>("the tenant filter hides another studio's draft");
    }

    [Fact]
    public async Task Resume_AnExpiredDraft_IsRefused()
    {
        (Studio studio, Guid artistId) = await SeedStudio();
        string email = $"has-account-{Guid.NewGuid():N}@example.test";
        Guid ownerId = ExistingClientAccount(email);
        await Book(studio, Request(email, artistId));
        TimeProvider eightDaysOn = new ShiftedTimeProvider(TimeSpan.FromDays(8));

        ResumeBookingDraftResponse result = await Resume(studio.Id, ownerId, MailedToken(), eightDaysOn);

        result.Outcome.Should().Be(ResumeBookingDraftHandler.Expired);
        result.Draft.Should().BeNull();
        (await Mine(studio.Id, ownerId)).Should().BeNull();
    }

    [Fact]
    public async Task CollisionResponse_IsByteIdentical_WhetherOrNotTheEmailHasAnAccount()
    {
        // The regression guard for the enumeration oracle (:75-87). Nothing in C05 should change the response; this
        // proves it, rather than leaving it to inspection.
        (Studio studio, Guid artistId) = await SeedStudio();

        string newEmail = $"new-{Guid.NewGuid():N}@example.test";
        Guid newUserId = Guid.NewGuid();
        _identity.GetUserIdByEmailAsync(newEmail, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(newEmail, Arg.Any<string>(), "client", studio.Id, "Jamie").Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(newEmail).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");
        GuestBookingAckResponse realBooking = await Book(studio, Request(newEmail, artistId));

        string takenEmail = $"taken-{Guid.NewGuid():N}@example.test";
        ExistingClientAccount(takenEmail);
        CreateGuestAppointmentRequest sameSlotOtherEmail = Request(takenEmail, artistId) with
        {
            Booking = Request(takenEmail, artistId).Booking with { Date = NextMondayAt(14) },
        };
        GuestBookingAckResponse collision = await Book(studio, sameSlotOtherEmail);

        byte[] a = JsonSerializer.SerializeToUtf8Bytes(realBooking, ApiJson);
        byte[] b = JsonSerializer.SerializeToUtf8Bytes(collision, ApiJson);
        b.Should().Equal(a);
        collision.GetType().Should().Be(realBooking.GetType());
    }

    private sealed class ShiftedTimeProvider(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }

    private async Task<(Studio Studio, Guid ArtistId)> SeedStudio()
    {
        await using AppDbContext seed = _fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Resume Studio",
            Slug = "resume-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            Timezone = "Europe/Tirane",
            OwnerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();

        Artist artist = new()
        {
            StudioId = studio.Id, FirstName = "Jane", LastName = "Doe",
            Email = $"artist-{Guid.NewGuid():N}@test.com", IsActive = true, HourlyRate = 100m,
        };
        seed.Artists.Add(artist);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            seed.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = studio.Id, ArtistId = artist.Id, DayOfWeek = day,
                StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0), IsAvailable = true,
            });
            seed.StudioHours.Add(new StudioHours
            {
                StudioId = studio.Id, DayOfWeek = day,
                StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0), IsOpen = true,
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
