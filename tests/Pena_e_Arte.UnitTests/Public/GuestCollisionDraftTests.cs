using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.BookingDrafts;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

/// <summary>
/// C05: the guest-booking collision path (an email that already has an account) saves what was typed as an
/// UNCLAIMED draft for that account and puts a resume link in the notice. The response stays the same Ack.
/// </summary>
public class GuestCollisionDraftTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly ILogger<CreateGuestAppointmentHandler> _logger = Substitute.For<ILogger<CreateGuestAppointmentHandler>>();
    private readonly BookingDraftResumeTokens _tokens = new(new EphemeralDataProtectionProvider());
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, 750, TimeSpan.Zero));
    private readonly Guid _ownerId = Guid.NewGuid();
    private const string Email = "has-an-account@example.com";
    private readonly Studio _studio;

    public GuestCollisionDraftTests()
    {
        _studio = new Studio
        {
            Name = "Collision Studio", Slug = "collision-studio", City = "Tirana",
            Timezone = "Europe/Tirane", IsActive = true, IsPublished = true,
        };
        _db.Studios.Add(_studio);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        _tenant.StudioId.Returns(_studio.Id);
        _appSettings.BaseUrl.Returns("https://app.tattooos.co");
        _identity.GetUserIdByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(_ownerId);
        _identity.GetUserRolesAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(new List<string> { "client" });
        _emailRenderer.RenderGuestBookingEmailCollision(Arg.Any<string>(), Arg.Any<string?>()).Returns("<html></html>");
    }

    private CreateGuestAppointmentHandler Sut() => new(
        _db, _tenant, _identity, Substitute.For<ISlotLocker>(), Substitute.For<IJobScheduler>(),
        Substitute.For<IRealtimeNotifier>(), Substitute.For<MediatR.ISender>(), Substitute.For<IPlanLimitService>(),
        _emailRenderer, _notifications, _appSettings, _tokens, _time, _logger);

    private static CreateGuestAppointmentRequest Request(string? safetyNotes = "Allergic to latex") => new(
        "Jamie", "Guest", Email, "+355691234567", MarketingOptIn: false,
        Booking: new CreateAppointmentRequest(
            ArtistId: null,
            ClientId: Guid.Empty,
            Date: new DateTime(2026, 12, 2, 13, 30, 0, DateTimeKind.Utc),
            DurationMinutes: 120,
            Notes: "Prefer afternoons",
            TattooDescription: "A fine-line swallow",
            Style: "FineLine",
            SafetyNotes: safetyNotes,
            DesiredPlacementLocations: ["left_forearm"],
            Images: [new AppointmentImageRequest("https://media.tattooos.co/x/appointments/pending/a.jpg", "AreaPhoto")]));

    private Task<GuestBookingAckResponse> Book(CreateGuestAppointmentRequest? request = null) =>
        Sut().Handle(new CreateGuestAppointmentCommand(_studio.Slug, request ?? Request()), default);

    private string? SentResumeUrl()
    {
        NSubstitute.Core.ICall call = _emailRenderer.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IEmailRenderer.RenderGuestBookingEmailCollision));
        return (string?)call.GetArguments()[1];
    }

    [Fact]
    public async Task Collision_SavesAnUnclaimedDraftWithoutTheSafetyNotes_AndLinksIt()
    {
        GuestBookingAckResponse ack = await Book();

        ack.Message.Should().Be("Thanks — check your email to continue.");
        BookingDraft draft = _db.BookingDrafts.Single();
        draft.UserId.Should().Be(_ownerId);
        draft.StudioId.Should().Be(_studio.Id);
        draft.ClaimedAt.Should().BeNull();
        draft.CreatedAt.Ticks.Should().Be(new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc).Ticks, "whole seconds");
        draft.ExpiresAt.Should().Be(draft.CreatedAt + BookingDraft.Lifetime);
        draft.PayloadJson.Should().NotContainEquivalentOf("safetyNotes");
        draft.PayloadJson.Should().NotContain("Allergic to latex");
        // Studio wall clock (Europe/Tirane is UTC+1 in December), as the form's date field holds it.
        draft.PayloadJson.Should().Contain("\"scheduledAt\":\"2026-12-02T14:30\"");
        draft.PayloadJson.Should().Contain("\"bookAnyArtist\":true");
        draft.PayloadJson.Should().Contain("A fine-line swallow");
        _db.BookingDraftAttachments.Single().Category.Should().Be(AppointmentAttachmentCategory.AreaPhoto);

        string? url = SentResumeUrl();
        url.Should().StartWith("https://app.tattooos.co/booking-drafts/resume?studio=collision-studio#t=");
        _tokens.TryRead(url![(url.IndexOf("#t=", StringComparison.Ordinal) + 3)..], out BookingDraftResumeRef resume)
            .Should().BeTrue();
        resume.Matches(draft).Should().BeTrue();
    }

    [Fact]
    public async Task Collision_TheUnclaimedDraftIsInvisibleToItsOwner()
    {
        await Book();

        BookingDraftResponse? seen = await new GetMyBookingDraftHandler(_db, new FakeCurrentUser(_ownerId, "client"), _time)
            .Handle(new GetMyBookingDraftQuery(), default);

        seen.Should().BeNull();
    }

    [Fact]
    public async Task Collision_NeverOverwritesADraftTheOwnerSavedThemselves_AndSendsNoLink()
    {
        BookingDraft own = new()
        {
            StudioId = _studio.Id, UserId = _ownerId, PayloadJson = """{"intake":{"tattooDescription":"mine"}}""",
            ExpiresAt = DateTime.UtcNow.AddDays(3), ClaimedAt = DateTime.UtcNow.AddDays(-1),
        };
        _db.BookingDrafts.Add(own);
        await _db.SaveChangesAsync();

        GuestBookingAckResponse ack = await Book();

        ack.Message.Should().Be("Thanks — check your email to continue.");
        BookingDraft only = _db.BookingDrafts.Single();
        only.Id.Should().Be(own.Id);
        only.PayloadJson.Should().Be("""{"intake":{"tattooDescription":"mine"}}""");
        SentResumeUrl().Should().BeNull("the notice goes out unchanged, with no token minted");
    }

    [Fact]
    public async Task Collision_ReplacesAnUnclaimedDraft_WithANewRow()
    {
        await Book();
        Guid firstId = _db.BookingDrafts.Single().Id;
        _emailRenderer.ClearReceivedCalls();
        _time.Advance(TimeSpan.FromMinutes(5));

        await Book(Request() with { Booking = Request().Booking with { TattooDescription = "A second try" } });

        BookingDraft only = _db.BookingDrafts.Single();
        only.Id.Should().NotBe(firstId, "a link sent for the replaced draft must stop matching");
        only.PayloadJson.Should().Contain("A second try");
        only.ClaimedAt.Should().BeNull();
        SentResumeUrl().Should().NotBeNull();
    }

    [Fact]
    public async Task Collision_ForAStaffAccount_SavesNothingAndSendsNoLink()
    {
        _identity.GetUserRolesAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(new List<string> { "owner" });

        GuestBookingAckResponse ack = await Book();

        ack.Message.Should().Be("Thanks — check your email to continue.");
        _db.BookingDrafts.Should().BeEmpty();
        SentResumeUrl().Should().BeNull();
    }

    [Fact]
    public async Task Collision_WhenSavingTheDraftFails_StillAcksAndStillSendsTheNotice()
    {
        _identity.GetUserRolesAsync(_ownerId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        GuestBookingAckResponse ack = await Book();

        ack.Message.Should().Be("Thanks — check your email to continue.");
        SentResumeUrl().Should().BeNull();
        await _notifications.Received(1).SendEmailAsync(Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collision_NeverLogsTheEmailAddress()
    {
        _identity.GetUserRolesAsync(_ownerId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        await Book();

        foreach (NSubstitute.Core.ICall call in _logger.ReceivedCalls())
            foreach (object? arg in call.GetArguments())
                (arg?.ToString() ?? string.Empty).Should().NotContain(Email);
    }
}
