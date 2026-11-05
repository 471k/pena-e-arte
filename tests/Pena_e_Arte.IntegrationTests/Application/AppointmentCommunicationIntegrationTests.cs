using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E04b (A08) on real MySQL: the new NotificationLog.AppointmentId column exists and the aftercare step
/// fills it per channel, and the reschedule task reads the audit log and staff messages correctly.
/// </summary>
[Collection("Database")]
public class AppointmentCommunicationIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Aftercare_RecordsEachChannel_AndTheAppointmentShowsSentOrFailed()
    {
        Seeded s = await Seed(AppointmentStatus.Completed);
        INotificationPreferenceService prefs = Substitute.For<INotificationPreferenceService>();
        prefs.IsEnabledAsync(s.StudioId, NotificationType.Aftercare, Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()).Returns(true);
        INotificationService notifications = Substitute.For<INotificationService>();
        notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("resend down"));

        await using (AppDbContext db = fixture.CreateDbContext(s.StudioId))
        {
            await new SendAftercareNotificationHandler(db, Substitute.For<IEmailRenderer>(), notifications, prefs,
                    NullLogger<SendAftercareNotificationHandler>.Instance)
                .Handle(new SendAftercareNotificationCommand(s.AppointmentId), default);
        }

        AftercareCommunicationResponse aftercare = (await Read(s)).Aftercare!;
        aftercare.State.Should().Be("Sent", "the text went out although the email failed");
        aftercare.Channels.Select(c => (c.Channel, c.Sent)).Should().BeEquivalentTo(new[] { ("Email", false), ("Sms", true) });
        aftercare.RanAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ARealReschedule_IsATask_UntilStaffMessageTheClientAboutIt()
    {
        Seeded s = await Seed(AppointmentStatus.Confirmed);

        // A real reschedule through the handler; AuditLogBehavior isn't in this pipeline, so the audit row
        // it would write is added the way the behaviour adds it.
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        ICurrentUser owner = Substitute.For<ICurrentUser>();
        owner.Role.Returns("owner");
        await using (AppDbContext db = fixture.CreateDbContext(s.StudioId))
        {
            await new RescheduleAppointmentHandler(db, tenant, owner, Substitute.For<IRealtimeNotifier>(), Substitute.For<ISender>(), Substitute.For<IJobScheduler>())
                .Handle(new RescheduleAppointmentCommand(s.AppointmentId,
                    new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(20), 60, null)), default);
            db.AuditLogEntries.Add(AuditLogEntry.Create(Guid.NewGuid(), "owner", AuditActions.AppointmentRescheduled,
                AuditTargetTypes.Appointment, s.AppointmentId, s.StudioId, "{}"));
            await db.SaveChangesAsync();
        }

        (await Read(s)).Reschedule!.State.Should().Be("NotSent");

        await using (AppDbContext db = fixture.CreateDbContext(s.StudioId))
        {
            Guid ownerUserId = Guid.NewGuid();
            Conversation conversation = Conversation.Create(s.StudioId, ownerUserId, "owner", Guid.NewGuid(), "client");
            db.Conversations.Add(conversation);
            db.ChatMessages.Add(ChatMessage.Create(s.StudioId, conversation.Id, ownerUserId, "owner", "Your session moved", s.AppointmentId));
            await db.SaveChangesAsync();
        }

        RescheduleCommunicationResponse after = (await Read(s)).Reschedule!;
        after.State.Should().Be("Sent");
        after.MessagedAt.Should().NotBeNull();
    }

    private async Task<AppointmentCommunicationResponse> Read(Seeded s)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        ICurrentUser owner = Substitute.For<ICurrentUser>();
        owner.Role.Returns("owner");
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetAppointmentCommunicationHandler(db, owner, tenant)
            .Handle(new GetAppointmentCommunicationQuery(s.AppointmentId), default);
    }

    private sealed record Seeded(Guid StudioId, Guid AppointmentId);

    private async Task<Seeded> Seed(AppointmentStatus status)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Comms", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = studioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = studioId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test", Phone = "+351912345678" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        DateTime start = status == AppointmentStatus.Completed ? DateTime.UtcNow.AddHours(-3) : DateTime.UtcNow.AddDays(5);
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = status,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();
        return new Seeded(studioId, appointment.Id);
    }
}
