using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class SendDesignReviewNotificationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier              _realtime      = Substitute.For<IRealtimeNotifier>();
    private readonly INotificationPreferenceService  _prefs         = new AlwaysEnabledNotificationPreferences();

    public SendDesignReviewNotificationHandlerTests()
    {
        _emailRenderer
            .RenderDesignApproved(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns("<html>approved</html>");
        _emailRenderer
            .RenderDesignChangesRequested(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns("<html>changes</html>");
    }

    private SendDesignReviewNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _prefs, _realtime,
            NullLogger<SendDesignReviewNotificationHandler>.Instance);

    private async Task<(Guid revisionId, Studio studio, Artist artist)> SeedData(
        bool approved = true, string? artistEmail = "artist@test.com", string ownerEmail = "owner@test.com")
    {
        Studio studio = new() { Name = "Test Studio", Slug = "test", OwnerEmail = ownerEmail };
        _db.Studios.Add(studio);

        Artist artist = new()
        {
            StudioId  = studio.Id,
            FirstName = "Marco",
            LastName  = "Ink",
            Email     = artistEmail ?? string.Empty,
        };
        _db.Artists.Add(artist);

        Design design = new()
        {
            StudioId  = studio.Id,
            ArtistId  = artist.Id,
            ClientId  = Guid.NewGuid(),
            Title     = "Dragon Back Piece",
            Artist    = artist,
        };
        _db.Designs.Add(design);

        DesignApproval? approval = approved
            ? new DesignApproval
            {
                StudioId         = studio.Id,
                DesignRevisionId = Guid.Empty,
                Status           = DesignApprovalStatus.Approved,
                ClientNotes      = "Looks great!",
                ReviewedAt       = DateTime.UtcNow,
            }
            : new DesignApproval
            {
                StudioId         = studio.Id,
                DesignRevisionId = Guid.Empty,
                Status           = DesignApprovalStatus.ChangesRequested,
                ClientNotes      = "Fix the shading",
                ReviewedAt       = DateTime.UtcNow,
            };

        DesignRevision revision = new()
        {
            StudioId      = studio.Id,
            DesignId      = design.Id,
            Design        = design,
            Approval      = approval,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow,
        };
        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return (revision.Id, studio, artist);
    }

    [Fact]
    public async Task Handle_Approved_SendsApprovedEmailToStudio()
    {
        (Guid revisionId, Studio studio, _) = await SeedData(approved: true);

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await _notifications.Received(1)
            .SendEmailAsync(studio.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _emailRenderer.Received(1)
            .RenderDesignApproved(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Handle_NotApproved_SendsChangesRequestedEmailToStudio()
    {
        (Guid revisionId, _, _) = await SeedData(approved: false);

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: false), default);

        _emailRenderer.Received(1)
            .RenderDesignChangesRequested(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Handle_ArtistEmailDiffersFromOwner_AlsoSendsToArtist()
    {
        (Guid revisionId, _, Artist artist) = await SeedData(
            artistEmail: "artist@other.com", ownerEmail: "owner@test.com");

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await _notifications.Received(1)
            .SendEmailAsync(artist.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ArtistEmailSameAsOwner_DoesNotSendDuplicate()
    {
        const string sharedEmail = "owner-artist@test.com";
        (Guid revisionId, _, _) = await SeedData(artistEmail: sharedEmail, ownerEmail: sharedEmail);

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await _notifications.Received(1)
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ArtistEmailEmpty_DoesNotSendToArtist()
    {
        (Guid revisionId, _, _) = await SeedData(artistEmail: null, ownerEmail: "owner@test.com");

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await _notifications.Received(1)
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RevisionNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendDesignReviewNotificationCommand(Guid.NewGuid(), Approved: true), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid revisionId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid revisionId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        NotificationLog? log = await _db.NotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_WritesSuccessNotificationLog()
    {
        (Guid revisionId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Studio);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studio.Id);
    }

    [Fact]
    public async Task Handle_ValidInput_WritesArtistNotificationLog()
    {
        (Guid revisionId, _, Artist artist) = await SeedData(
            artistEmail: "artist@other.com", ownerEmail: "owner@test.com");

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        NotificationLog? artistLog = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Artist);
        artistLog.Should().NotBeNull();
        artistLog!.RecipientId.Should().Be(artist.Id);
    }

    [Fact]
    public async Task Handle_ValidInput_PushesNotificationReceivedEvent()
    {
        (Guid revisionId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}


