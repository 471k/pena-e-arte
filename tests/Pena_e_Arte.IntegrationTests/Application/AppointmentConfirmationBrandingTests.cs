using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services.MailKit;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class AppointmentConfirmationBrandingTests(DatabaseFixture fixture)
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private SendAppointmentConfirmationHandler CreateSut(AppDbContext db) =>
        new(db, new EmailRenderer(), _notifications,
            NullLogger<SendAppointmentConfirmationHandler>.Instance);

    [Fact]
    public async Task Handle_StudioBrandingTrue_EmailBodyContainsBrandingFooter()
    {
        (Guid appointmentId, Guid studioId) = await SeedData(showPlatformBranding: true);

        string capturedBody = string.Empty;
        _notifications
            .When(n => n.SendEmailAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedBody = call.ArgAt<string>(2));

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await CreateSut(db).Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        capturedBody.Should().Contain("penaearte.com");
    }

    [Fact]
    public async Task Handle_StudioBrandingFalse_EmailBodyDoesNotContainBrandingFooter()
    {
        (Guid appointmentId, Guid studioId) = await SeedData(showPlatformBranding: false);

        string capturedBody = string.Empty;
        _notifications
            .When(n => n.SendEmailAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => capturedBody = call.ArgAt<string>(2));

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await CreateSut(db).Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        capturedBody.Should().NotBeNullOrEmpty();
        capturedBody.Should().NotContain("penaearte.com");
    }

    [Fact]
    public async Task Handle_ValidAppointment_SubjectContainsConfirmed()
    {
        (Guid appointmentId, Guid studioId) = await SeedData(showPlatformBranding: true);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        await CreateSut(db).Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Confirmed")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private async Task<(Guid AppointmentId, Guid StudioId)> SeedData(bool showPlatformBranding)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new()
        {
            Name     = "Branding Test Studio",
            Slug     = ("cb-" + Guid.NewGuid().ToString("N"))[..20],
            City     = "Porto",
            IsActive = true,
        };
        if (!showPlatformBranding) studio.UpdateBranding(false);
        ctx.Studios.Add(studio);
        await ctx.SaveChangesAsync();

        await using AppDbContext tenantCtx = fixture.CreateDbContext(studio.Id);

        Client client = new()
        {
            StudioId  = studio.Id,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@example.com",
        };
        tenantCtx.Clients.Add(client);
        await tenantCtx.SaveChangesAsync();

        Artist artist = new()
        {
            StudioId  = studio.Id,
            FirstName = "João",
            LastName  = "Artista",
            Email     = "joao@example.com",
        };
        tenantCtx.Artists.Add(artist);
        await tenantCtx.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId        = studio.Id,
            ArtistId        = artist.Id,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
            DepositAmount   = 50m,
        };
        tenantCtx.Appointments.Add(appointment);
        await tenantCtx.SaveChangesAsync();

        return (appointment.Id, studio.Id);
    }
}
