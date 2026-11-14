using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Services.Waitlists;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Waitlists;

public class CompleteWaitlistClaimHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly WaitlistClaimTokens _tokens = new(new EphemeralDataProtectionProvider());
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly DateTime _slot = BookableSlotSeeder.FutureSlot();
    private readonly Client _client;

    public CompleteWaitlistClaimHandlerTests()
    {
        BookableSlotSeeder.SeedAlwaysBookableArtist(_db, _studioId, _artistId);
        _client = new Client { StudioId = _studioId, FirstName = "Ana", LastName = "K", Email = "ana@example.com" };
        _db.Clients.Add(_client);
        _db.SaveChanges();
    }

    private Task Complete(string token, Guid? clientId = null, Guid? studioId = null) =>
        new CompleteWaitlistClaimHandler(_db, _tokens, _sender, NullLogger<CompleteWaitlistClaimHandler>.Instance)
            .Handle(new CompleteWaitlistClaimCommand(studioId ?? _studioId, clientId ?? _client.Id, token), default);

    private WaitlistStatus StatusOf(Waitlist entry) => _db.WaitlistEntries.Single(w => w.Id == entry.Id).Status;

    [Fact]
    public async Task SignedInClientsOwnEntry_IsMarkedBooked()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);

        await Complete(_tokens.Mint(entry));

        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
    }

    [Fact]
    public async Task GuestEntry_IsMarkedBooked_WhenTheBookingUsesTheSameEmail()
    {
        Waitlist entry = SeedNotified(guestEmail: "ANA@example.com ");

        await Complete(_tokens.Mint(entry));

        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
    }

    [Fact]
    public async Task ForwardedLink_BookingForSomeoneElse_LeavesTheEntryAlone()
    {
        Waitlist entry = SeedNotified(guestEmail: "the-real-person@example.com");

        await Complete(_tokens.Mint(entry));

        StatusOf(entry).Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task TokenForAnotherStudio_IsIgnored()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);

        await Complete(_tokens.Mint(entry), studioId: Guid.NewGuid());

        StatusOf(entry).Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task InvalidToken_IsIgnored_AndNeverThrows()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);

        Func<Task> act = () => Complete("garbage");

        await act.Should().NotThrowAsync();
        StatusOf(entry).Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task SecondUse_ChangesNothingMore()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);
        string token = _tokens.Mint(entry);
        await Complete(token);
        _sender.ClearReceivedCalls();

        await Complete(token);

        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
        await _sender.DidNotReceiveWithAnyArgs().Send(default(SendWaitlistSlotAvailableNotificationCommand)!, default);
    }

    [Fact]
    public async Task BookedTheOfferedSlot_NothingCascades()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);
        Waitlist nextInLine = SeedWaiting();
        // The booking this claim came from, already saved: it occupies the offered slot.
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slot);

        await Complete(_tokens.Mint(entry));

        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
        StatusOf(nextInLine).Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task BookedAnotherTime_TheOfferedSlotMovesToTheNextPerson()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);
        Waitlist nextInLine = SeedWaiting();
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slot.AddHours(5));

        await Complete(_tokens.Mint(entry));

        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
        StatusOf(nextInLine).Should().Be(WaitlistStatus.Notified);
        await _sender.Received(1).Send(
            Arg.Is<SendWaitlistSlotAvailableNotificationCommand>(c => c.WaitlistEntryId == nextInLine.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotificationFailure_DoesNotSurfaceToTheBooking()
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);
        SeedWaiting();
        _sender.Send(Arg.Any<SendWaitlistSlotAvailableNotificationCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mail down"));

        Func<Task> act = () => Complete(_tokens.Mint(entry));

        await act.Should().NotThrowAsync();
        StatusOf(entry).Should().Be(WaitlistStatus.Booked);
    }

    [Theory]
    [InlineData(WaitlistStatus.Expired)]
    [InlineData(WaitlistStatus.Cancelled)]
    public async Task ClosedEntry_IsNotReopenedAsBooked(WaitlistStatus status)
    {
        Waitlist entry = SeedNotified(clientId: _client.Id);
        string token = _tokens.Mint(entry);
        entry.Status = status;
        await _db.SaveChangesAsync(default);

        await Complete(token);

        StatusOf(entry).Should().Be(status);
    }

    private Waitlist SeedNotified(Guid? clientId = null, string? guestEmail = null)
    {
        DateTime now = DateTime.UtcNow.AddHours(-1);
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ClientId = clientId,
            GuestEmail = guestEmail,
            GuestName = guestEmail is null ? null : "Guest",
            PreferredDateFrom = _slot.AddDays(-1),
            PreferredDateTo = _slot.AddDays(1),
            Status = WaitlistStatus.Notified,
            NotifiedAt = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            NotifiedSlotStart = _slot,
            NotifiedArtistId = _artistId,
            NotifiedSlotDurationMinutes = 60,
        };
        _db.WaitlistEntries.Add(entry);
        _db.SaveChanges();
        return entry;
    }

    private Waitlist SeedWaiting()
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            GuestName = "Next",
            GuestEmail = "next@example.com",
            PreferredDateFrom = _slot.AddDays(-1),
            PreferredDateTo = _slot.AddDays(1),
            Status = WaitlistStatus.Waiting,
            CreatedAt = DateTime.UtcNow.AddHours(-3),
        };
        _db.WaitlistEntries.Add(entry);
        _db.SaveChanges();
        return entry;
    }
}
