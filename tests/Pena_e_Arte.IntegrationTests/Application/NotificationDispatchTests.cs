using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class NotificationDispatchTests
{
    private readonly DatabaseFixture               fixture;
    private readonly IEmailRenderer                 _renderer      = Substitute.For<IEmailRenderer>();
    private readonly INotificationService           _notifications = Substitute.For<INotificationService>();
    private readonly INotificationPreferenceService _prefs         = Substitute.For<INotificationPreferenceService>();
    private readonly IRealtimeNotifier              _realtime      = Substitute.For<IRealtimeNotifier>();

    public NotificationDispatchTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
        _prefs.IsEnabledAsync(default, default, default, default)
              .ReturnsForAnyArgs(Task.FromResult(true));
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<(Guid studioId, string ownerEmail)> SeedStudio()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Id         = tenantId,
            Name       = "Integration Studio",
            Slug       = tenantId.ToString("N")[..8],
            OwnerEmail = "owner@integration.test",
        };
        ctx.Studios.Add(studio);
        await ctx.SaveChangesAsync();
        return (tenantId, studio.OwnerEmail);
    }

    private async Task<Guid> SeedClient(Guid studioId, string? phone = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId  = studioId,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = $"{Guid.NewGuid():N}@test.com",
            Phone     = phone,
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedArtist(Guid studioId, string? email = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Artist artist = new()
        {
            StudioId  = studioId,
            FirstName = "Marco",
            LastName  = "Ink",
            Email     = email ?? $"{Guid.NewGuid():N}@artist.test",
        };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedAppointment(Guid studioId, Guid clientId)
    {
        Guid artistId = await SeedArtist(studioId);
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Appointment appointment = new()
        {
            StudioId        = studioId,
            ArtistId        = artistId,
            ClientId        = clientId,
            Date            = DateTime.UtcNow.AddDays(5),
            EndDate         = DateTime.UtcNow.AddDays(5).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Guid> SeedDesignRevision(Guid studioId, Guid clientId, bool approved)
    {
        Guid artistId = await SeedArtist(studioId, "artist@diff.test");
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);

        Design design = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = clientId,
            Title    = "Dragon Piece",
        };
        ctx.Designs.Add(design);
        await ctx.SaveChangesAsync();

        DesignApproval approval = new()
        {
            StudioId         = studioId,
            DesignRevisionId = Guid.Empty,
            Status           = approved ? DesignApprovalStatus.Approved : DesignApprovalStatus.ChangesRequested,
            ClientNotes      = approved ? "Looks great!" : "Fix the shading",
            ReviewedAt       = DateTime.UtcNow,
        };

        DesignRevision revision = new()
        {
            StudioId      = studioId,
            DesignId      = design.Id,
            Approval      = approval,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow,
        };
        ctx.DesignRevisions.Add(revision);
        await ctx.SaveChangesAsync();
        return revision.Id;
    }

    private async Task<Guid> SeedPayment(Guid studioId, Guid clientId, Guid appointmentId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Payment payment = new()
        {
            StudioId      = studioId,
            ClientId      = clientId,
            AppointmentId = appointmentId,
            Amount        = 100m,
            Status        = PaymentStatus.Paid,
            Method        = ClientPaymentMethod.Card,
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return payment.Id;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAppointmentCreatedNotification_ValidAppointment_WritesClientAndStudioLogs()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);
        Guid appointmentId = await SeedAppointment(studioId, clientId);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendAppointmentCreatedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendAppointmentCreatedNotificationHandler>.Instance)
            .Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        List<NotificationLog> logs = await verify.NotificationLogs
            .Where(n => n.StudioId == studioId)
            .ToListAsync();

        logs.Should().Contain(n => n.RecipientType == NotificationRecipientType.Client
                                && n.Channel == NotificationChannel.Email);
        logs.Should().Contain(n => n.RecipientType == NotificationRecipientType.Studio
                                && n.Channel == NotificationChannel.Email);
    }

    [Fact]
    public async Task SendAppointmentCreatedNotification_ClientWithPhone_WritesSmsLog()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId, phone: "+351912345678");
        Guid appointmentId = await SeedAppointment(studioId, clientId);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendAppointmentCreatedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendAppointmentCreatedNotificationHandler>.Instance)
            .Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool hasSmsLog = await verify.NotificationLogs
            .AnyAsync(n => n.StudioId == studioId && n.Channel == NotificationChannel.Sms);
        hasSmsLog.Should().BeTrue();
    }

    [Fact]
    public async Task SendDesignReviewNotification_Approved_WritesStudioAndArtistLogs()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);
        Guid revisionId    = await SeedDesignRevision(studioId, clientId, approved: true);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendDesignReviewNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendDesignReviewNotificationHandler>.Instance)
            .Handle(new SendDesignReviewNotificationCommand(revisionId, Approved: true), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        List<NotificationLog> logs = await verify.NotificationLogs
            .Where(n => n.StudioId == studioId)
            .ToListAsync();

        logs.Should().Contain(n => n.RecipientType == NotificationRecipientType.Studio);
        logs.Should().Contain(n => n.RecipientType == NotificationRecipientType.Artist);
    }

    [Fact]
    public async Task SendIntakeFormSubmittedNotification_ValidForm_WritesStudioLog()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);

        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        IntakeForm form = new()
        {
            StudioId    = studioId,
            ClientId    = clientId,
            FormData    = "{}",
            SubmittedAt = DateTime.UtcNow,
        };
        ctx.IntakeForms.Add(form);
        await ctx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendIntakeFormSubmittedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendIntakeFormSubmittedNotificationHandler>.Instance)
            .Handle(new SendIntakeFormSubmittedNotificationCommand(form.Id), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool exists = await verify.NotificationLogs
            .AnyAsync(n => n.StudioId == studioId
                        && n.RecipientType == NotificationRecipientType.Studio
                        && n.Channel == NotificationChannel.Email);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SendConsentFormSignedNotification_ValidForm_WritesStudioLog()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);
        Guid appointmentId = await SeedAppointment(studioId, clientId);

        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ConsentForm form = new()
        {
            StudioId      = studioId,
            ClientId      = clientId,
            AppointmentId = appointmentId,
            SignatureData = "data:image/png;base64,abc",
            SignedAt      = DateTime.UtcNow,
        };
        ctx.ConsentForms.Add(form);
        await ctx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendConsentFormSignedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendConsentFormSignedNotificationHandler>.Instance)
            .Handle(new SendConsentFormSignedNotificationCommand(form.Id), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool exists = await verify.NotificationLogs
            .AnyAsync(n => n.StudioId == studioId
                        && n.RecipientType == NotificationRecipientType.Studio
                        && n.Channel == NotificationChannel.Email);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SendDepositCapturedNotification_ValidPayment_WritesClientEmailLog()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);
        Guid appointmentId = await SeedAppointment(studioId, clientId);
        Guid paymentId     = await SeedPayment(studioId, clientId, appointmentId);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendDepositCapturedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendDepositCapturedNotificationHandler>.Instance)
            .Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool exists = await verify.NotificationLogs
            .AnyAsync(n => n.StudioId == studioId
                        && n.RecipientId == clientId
                        && n.RecipientType == NotificationRecipientType.Client
                        && n.Channel == NotificationChannel.Email);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SendPaymentRefundedNotification_ValidPayment_WritesClientEmailLog()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid clientId      = await SeedClient(studioId);
        Guid appointmentId = await SeedAppointment(studioId, clientId);
        Guid paymentId     = await SeedPayment(studioId, clientId, appointmentId);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendPaymentRefundedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendPaymentRefundedNotificationHandler>.Instance)
            .Handle(new SendPaymentRefundedNotificationCommand(paymentId), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool exists = await verify.NotificationLogs
            .AnyAsync(n => n.StudioId == studioId
                        && n.RecipientId == clientId
                        && n.RecipientType == NotificationRecipientType.Client
                        && n.Channel == NotificationChannel.Email);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NotificationHandler_EntityNotFound_WritesNoLogs()
    {
        (Guid studioId, _) = await SeedStudio();
        Guid bogusId        = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await new SendDepositCapturedNotificationHandler(
            db, _renderer, _notifications, _prefs, _realtime,
            NullLogger<SendDepositCapturedNotificationHandler>.Instance)
            .Handle(new SendDepositCapturedNotificationCommand(bogusId), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        int count = await verify.NotificationLogs.CountAsync(n => n.StudioId == studioId);
        count.Should().Be(0);
    }
}
