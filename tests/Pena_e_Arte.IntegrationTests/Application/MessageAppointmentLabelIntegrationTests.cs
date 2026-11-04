using System.Data.Common;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Messaging.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// "Which appointment is this message about", against real MySQL: the conversation stores only
/// the last message's appointment id and both read queries resolve the label from the appointment
/// row on every read — so a reschedule shows the new date straight away (the test that fails if
/// the label text were ever cached) — and the inbox resolves every row's label in one statement.
/// </summary>
[Collection("Database")]
public class MessageAppointmentLabelIntegrationTests(DatabaseFixture fixture)
{
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();

    [Fact]
    public async Task TheLabelFollowsTheAppointment_IncludingAfterAReschedule()
    {
        Seeded s = await Seed(conversations: 1);
        Guid conversationId = s.ConversationIds[0];
        Guid appointmentId = s.AppointmentIds[0];
        DateTime originalStart = s.Starts[0];

        await Send(s, conversationId, "About my booking", appointmentId);

        ConversationResponse row = (await Inbox(s, s.ClientUserId)).Single(c => c.Id == conversationId);
        row.LastMessageAppointmentId.Should().Be(appointmentId);
        row.LastMessageAppointment.Should().NotBeNull();
        row.LastMessageAppointment!.StartsAt.Should().BeCloseTo(originalStart, TimeSpan.FromSeconds(1));
        row.LastMessageAppointment.ServiceName.Should().Be("Fine line");
        ChatMessageResponse message = (await Thread(s, conversationId, s.ArtistUserId, "artist")).Single();
        message.Appointment.Should().BeEquivalentTo(row.LastMessageAppointment, "the artist reads the same label");

        // The studio moves the appointment with the real reschedule operation.
        DateTime newStart = originalStart.AddDays(5);
        await Reschedule(s, appointmentId, newStart);

        // Nothing touched the conversation or the message, yet both read the new date: only the id
        // is stored. If the label text were cached on either row, these would still show the old one.
        ConversationResponse afterRow = (await Inbox(s, s.ClientUserId)).Single(c => c.Id == conversationId);
        afterRow.LastMessageAppointment!.StartsAt.Should().BeCloseTo(newStart, TimeSpan.FromSeconds(1));
        ChatMessageResponse afterMessage = (await Thread(s, conversationId, s.ClientUserId)).Single();
        afterMessage.Appointment!.StartsAt.Should().BeCloseTo(newStart, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AConversationWithNoAppointmentReferences_ReturnsNullThroughout()
    {
        Seeded s = await Seed(conversations: 1);
        Guid conversationId = s.ConversationIds[0];

        await Send(s, conversationId, "hello", appointmentId: null);

        ConversationResponse row = (await Inbox(s, s.ClientUserId)).Single(c => c.Id == conversationId);
        row.LastMessageAppointmentId.Should().BeNull();
        row.LastMessageAppointment.Should().BeNull();
        ChatMessageResponse message = (await Thread(s, conversationId, s.ClientUserId)).Single();
        message.AppointmentId.Should().BeNull();
        message.Appointment.Should().BeNull();
    }

    [Fact]
    public async Task ALaterPlainMessage_ClearsTheInboxLabel_ButTheEarlierMessageKeepsItsOwn()
    {
        Seeded s = await Seed(conversations: 1);
        Guid conversationId = s.ConversationIds[0];

        await Send(s, conversationId, "About my booking", s.AppointmentIds[0]);
        await Send(s, conversationId, "Also, unrelated question", appointmentId: null);

        ConversationResponse row = (await Inbox(s, s.ClientUserId)).Single(c => c.Id == conversationId);
        row.LastMessageAppointmentId.Should().BeNull();
        row.LastMessageAppointment.Should().BeNull();
        List<ChatMessageResponse> thread = await Thread(s, conversationId, s.ClientUserId);
        thread.Select(m => m.Appointment?.AppointmentId).Should().Equal(s.AppointmentIds[0], null);
    }

    [Fact]
    public async Task TheInbox_QueryCountDoesNotScaleWithConversations()
    {
        Seeded one = await Seed(conversations: 1);
        Seeded many = await Seed(conversations: 8);
        for (int i = 0; i < one.ConversationIds.Count; i++)
            await Send(one, one.ConversationIds[i], "About my booking", one.AppointmentIds[i]);
        for (int i = 0; i < many.ConversationIds.Count; i++)
            await Send(many, many.ConversationIds[i], "About my booking", many.AppointmentIds[i]);

        int forOne = await CountInboxQueries(one);
        int forMany = await CountInboxQueries(many);

        forOne.Should().BeGreaterThan(0, "the counter must actually see the commands");
        forMany.Should().Be(forOne, "every row's label is resolved in one statement, not one per conversation");
        List<ConversationResponse> rows = await Inbox(many, many.ClientUserId);
        rows.Should().HaveCount(8).And.OnlyContain(c => c.LastMessageAppointment != null);
        rows.Select(c => c.LastMessageAppointment!.AppointmentId).Should().BeEquivalentTo(many.AppointmentIds);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task Send(Seeded s, Guid conversationId, string body, Guid? appointmentId)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        ICurrentUser client = UserFor(s.ClientUserId, "client");
        await new SendChatMessageHandler(db, client, tenant, _realtime, _jobs).Handle(
            new SendChatMessageCommand(conversationId, new SendChatMessageRequest(body, appointmentId)), default);
    }

    private async Task<List<ConversationResponse>> Inbox(Seeded s, Guid viewerUserId)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetConversationsHandler(db, UserFor(viewerUserId, "client"), Substitute.For<IIdentityService>())
            .Handle(new GetConversationsQuery(), default);
    }

    private async Task<List<ChatMessageResponse>> Thread(Seeded s, Guid conversationId, Guid viewerUserId, string viewerRole = "client")
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetConversationMessagesHandler(db, UserFor(viewerUserId, viewerRole))
            .Handle(new GetConversationMessagesQuery(conversationId, null, 30), default);
    }

    private async Task Reschedule(Seeded s, Guid appointmentId, DateTime newStart)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        await using AppDbContext db = fixture.CreateDbContext(tenant);
        RescheduleAppointmentHandler handler = new(
            db, tenant, UserFor(Guid.NewGuid(), "owner"), _realtime, Substitute.For<ISender>(), _jobs);
        await handler.Handle(new RescheduleAppointmentCommand(
            appointmentId, new RescheduleAppointmentRequest(newStart, 60, null)), default);
    }

    private async Task<int> CountInboxQueries(Seeded s)
    {
        CommandCounter counter = new();
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        await using AppDbContext db = new(options, tenant);

        await new GetConversationsHandler(db, UserFor(s.ClientUserId, "client"), Substitute.For<IIdentityService>())
            .Handle(new GetConversationsQuery(), default);

        return counter.Count;
    }

    private static ICurrentUser UserFor(Guid userId, string role)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.Role.Returns(role);
        return user;
    }

    private sealed record Seeded(
        Guid StudioId, Guid ClientUserId, Guid ArtistUserId,
        List<Guid> ConversationIds, List<Guid> AppointmentIds, List<DateTime> Starts);

    /// <summary>One client with one appointment per conversation; each conversation is with a
    /// different artist, all of whom are in the studio.</summary>
    private async Task<Seeded> Seed(int conversations)
    {
        Guid studioId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Labels", Slug = studioId.ToString("N")[..8], CountryCode = "AL", Currency = "ALL" });
        Client client = new() { StudioId = studioId, UserId = clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        Service service = new() { StudioId = studioId, Name = "Fine line", DurationMinutes = 60, IsActive = true };
        ctx.Clients.Add(client);
        ctx.Services.Add(service);

        List<Guid> conversationIds = [];
        List<Guid> appointmentIds = [];
        List<DateTime> starts = [];
        Guid firstArtistUserId = Guid.Empty;
        for (int i = 0; i < conversations; i++)
        {
            Guid artistUserId = Guid.NewGuid();
            if (i == 0) firstArtistUserId = artistUserId;
            Artist artist = new()
            {
                StudioId = studioId,
                UserId = artistUserId,
                FirstName = "A",
                LastName = $"{i}",
                Email = $"{Guid.NewGuid():N}@a.test",
                IsActive = true,
            };
            ctx.Artists.Add(artist);

            DateTime start = DateTime.UtcNow.Date.AddDays(10 + i).AddHours(13);
            Appointment appointment = new()
            {
                StudioId = studioId,
                ClientId = client.Id,
                ArtistId = artist.Id,
                ServiceId = service.Id,
                Date = start,
                EndDate = start.AddHours(1),
                DurationMinutes = 60,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Pending,
            };
            ctx.Appointments.Add(appointment);

            Conversation conversation = Conversation.Create(studioId, clientUserId, "client", artistUserId, "artist");
            ctx.Conversations.Add(conversation);

            conversationIds.Add(conversation.Id);
            appointmentIds.Add(appointment.Id);
            starts.Add(start);
        }
        await ctx.SaveChangesAsync();

        return new Seeded(studioId, clientUserId, firstArtistUserId, conversationIds, appointmentIds, starts);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }
}
