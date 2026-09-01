using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class CreateGuestAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly ISlotLocker _slotLocker = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly MediatR.ISender _sender = Substitute.For<MediatR.ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly ILogger<CreateGuestAppointmentHandler> _logger = Substitute.For<ILogger<CreateGuestAppointmentHandler>>();

    private CreateGuestAppointmentHandler CreateSut() => new(
        _db, _identity, _slotLocker, _jobs, _realtime, _sender, _planLimits,
        _emailRenderer, _notifications, _appSettings, _logger);

    public CreateGuestAppointmentHandlerTests()
    {
        _slotLocker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                   .Returns(true);
        _appSettings.BaseUrl.Returns("https://tattooos.co");
        _emailRenderer.RenderGuestBookingWelcome(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                      .Returns("<html></html>");
        _emailRenderer.RenderGuestBookingEmailCollision(Arg.Any<string>())
                      .Returns("<html></html>");
    }

    private Studio SeedStudioWithAvailableArtist()
    {
        Studio studio = new()
        {
            Name = "Guest Studio",
            Slug = "guest-studio",
            City = "Porto",
            IsActive = true,
            IsPublished = true,
        };
        _db.Studios.Add(studio);

        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Luna",
            LastName = "Artista",
            Email = "luna@test.com",
            IsActive = true,
            HourlyRate = 80,
        };
        _db.Artists.Add(artist);

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            _db.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = studio.Id,
                ArtistId = artist.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23),
                IsAvailable = true,
            });
        }
        _db.SaveChangesAsync().GetAwaiter().GetResult();

        _lastSeededArtistId = artist.Id;
        return studio;
    }

    private Guid _lastSeededArtistId;

    private static DateTime NextMondayAt(int hour)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date.AddHours(hour);
    }

    private CreateGuestAppointmentRequest ValidRequest(Studio studio, string email) => new(
        "Jamie", "Guest", email, "+351912345678", MarketingOptIn: true,
        Booking: new CreateAppointmentRequest(
            _lastSeededArtistId, Guid.Empty, NextMondayAt(10), 60, null, "A small rose"));

    [Fact]
    public async Task Handle_ValidRequest_CreatesIdentityUserWithGeneratedPassword()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "jamie@example.com";
        Guid newUserId = Guid.NewGuid();
        string? capturedPassword = null;

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(email, Arg.Do<string>(p => capturedPassword = p), "client", studio.Id, "Jamie")
                 .Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");

        GuestBookingAckResponse result = await CreateSut().Handle(
            new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        result.Should().NotBeNull();
        capturedPassword.Should().NotBeNullOrEmpty();
        capturedPassword!.Length.Should().BeGreaterThanOrEqualTo(24);
    }

    [Fact]
    public async Task Handle_ValidRequest_SendsWelcomeEmail()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "jamie@example.com";
        Guid newUserId = Guid.NewGuid();

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(email, Arg.Any<string>(), "client", studio.Id, "Jamie")
                 .Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");

        await CreateSut().Handle(new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        await _notifications.Received(1).SendEmailAsync(
            email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Enumeration-resistance (2026-09-01, /code-review finding): a colliding email must return
    // the exact same ack as a real success — never a distinct exception/status the caller could
    // use as an account-existence oracle. Disambiguation happens only via the email sent.
    [Fact]
    public async Task Handle_DuplicateEmail_ReturnsTheSameAckAsSuccess()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "existing@example.com";
        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        GuestBookingAckResponse result = await CreateSut().Handle(
            new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        result.Should().NotBeNull();
        result.Message.Should().Be("Thanks — check your email to continue.");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_NeverCallsCreateUser()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "existing@example.com";
        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        await CreateSut().Handle(
            new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        await _identity.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_DuplicateEmail_SendsCollisionNoticeNotWelcomeEmail()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "existing@example.com";
        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        await CreateSut().Handle(
            new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        _emailRenderer.Received(1).RenderGuestBookingEmailCollision(studio.Name);
        _emailRenderer.DidNotReceive().RenderGuestBookingWelcome(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _notifications.Received(1).SendEmailAsync(
            email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownStudioSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CreateGuestAppointmentCommand("no-such-slug", ValidRequest(new Studio { Slug = "x" }, "a@b.com")),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ValidRequest_NeverLogsTheGeneratedPassword()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "jamie@example.com";
        Guid newUserId = Guid.NewGuid();
        string? capturedPassword = null;

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(email, Arg.Do<string>(p => capturedPassword = p), "client", studio.Id, "Jamie")
                 .Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");

        await CreateSut().Handle(new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        capturedPassword.Should().NotBeNullOrEmpty();

        // Inspect every argument of every call made to the ILogger interface (the extension
        // methods LogInformation/LogWarning/LogError all funnel through Log<TState>) and assert
        // the generated password never appears anywhere in a logged value.
        foreach (NSubstitute.Core.ICall call in _logger.ReceivedCalls())
        {
            foreach (object? arg in call.GetArguments())
            {
                arg?.ToString().Should().NotContain(capturedPassword,
                    "the guest's randomly generated password must never appear in any log output");
            }
        }
    }

    [Fact]
    public async Task Handle_EmailSendFailure_StillReturnsSuccessfully()
    {
        Studio studio = SeedStudioWithAvailableArtist();
        string email = "jamie@example.com";
        Guid newUserId = Guid.NewGuid();

        _identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        _identity.CreateUserAsync(email, Arg.Any<string>(), "client", studio.Id, "Jamie")
                 .Returns((true, newUserId, Array.Empty<string>()));
        _identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        _identity.GenerateEmailConfirmationTokenAsync(newUserId).Returns("confirm-token");
        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns<Task>(_ => throw new InvalidOperationException("SMTP down"));

        GuestBookingAckResponse result = await CreateSut().Handle(
            new CreateGuestAppointmentCommand(studio.Slug, ValidRequest(studio, email)), default);

        result.Should().NotBeNull();
    }
}
