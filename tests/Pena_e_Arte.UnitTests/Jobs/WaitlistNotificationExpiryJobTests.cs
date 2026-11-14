using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class WaitlistNotificationExpiryJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly DateTime _slotDate = BookableSlotSeeder.FutureSlot();

    public WaitlistNotificationExpiryJobTests() =>
        BookableSlotSeeder.SeedAlwaysBookableArtist(_db, _studioId, _artistId);

    private WaitlistNotificationExpiryJob CreateSut() =>
        new(_db, _tenant, _sender, NullLogger<WaitlistNotificationExpiryJob>.Instance);

    [Fact]
    public async Task RunAsync_NotifiedPastClaimWindow_ExpiresEntry()
    {
        Waitlist entry = await SeedNotified(DateTime.UtcNow.AddHours(-25));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == entry.Id).Status.Should().Be(WaitlistStatus.Expired);
    }

    [Fact]
    public async Task RunAsync_NotifiedWithinClaimWindow_DoesNotExpire()
    {
        Waitlist entry = await SeedNotified(DateTime.UtcNow.AddHours(-2));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == entry.Id).Status.Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task RunAsync_ExpiredEntry_CascadesToNextWaitingEntryInLine()
    {
        await SeedNotified(DateTime.UtcNow.AddHours(-25));
        Waitlist nextInLine = await SeedWaiting(createdAt: DateTime.UtcNow.AddHours(-10));

        await CreateSut().RunAsync();

        Waitlist next = _db.WaitlistEntries.Single(w => w.Id == nextInLine.Id);
        next.Status.Should().Be(WaitlistStatus.Notified);
        next.NotifiedSlotStart.Should().Be(_slotDate);
        await _sender.Received(1).Send(
            Arg.Is<SendWaitlistSlotAvailableNotificationCommand>(c => c.WaitlistEntryId == nextInLine.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ExpiredEntryWithNoOneElseWaiting_NoCascade()
    {
        await SeedNotified(DateTime.UtcNow.AddHours(-25));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Count(w => w.Status == WaitlistStatus.Notified).Should().Be(0);
    }

    // ── C09 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CascadesOnTheOfferedSlot_NotTheExpiredClientsOwnWindow()
    {
        // The expired client's window opens two days before the slot. The old job cascaded on that start, so it
        // matched people whose windows covered a day nobody had offered. This person only wants the slot's day.
        await SeedNotified(DateTime.UtcNow.AddHours(-25), preferredFrom: _slotDate.AddDays(-2));
        Waitlist wantsTheSlotDay = await SeedWaiting(
            createdAt: DateTime.UtcNow.AddHours(-10), from: _slotDate.AddHours(-1), to: _slotDate.AddHours(1));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == wantsTheSlotDay.Id).Status.Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task RunAsync_OfferedSlotTakenSince_ExpiresButDoesNotCascade()
    {
        Waitlist expired = await SeedNotified(DateTime.UtcNow.AddHours(-25));
        Waitlist nextInLine = await SeedWaiting(createdAt: DateTime.UtcNow.AddHours(-10));
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slotDate);

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == expired.Id).Status.Should().Be(WaitlistStatus.Expired);
        _db.WaitlistEntries.Single(w => w.Id == nextInLine.Id).Status.Should().Be(WaitlistStatus.Waiting);
        await _sender.DidNotReceiveWithAnyArgs().Send(default(SendWaitlistSlotAvailableNotificationCommand)!, default);
    }

    [Fact]
    public async Task RunAsync_NoRecordedSlot_ExpiresButDoesNotGuessACascade()
    {
        Waitlist preC09 = await SeedNotified(DateTime.UtcNow.AddHours(-25), recordSlot: false);
        Waitlist nextInLine = await SeedWaiting(createdAt: DateTime.UtcNow.AddHours(-10));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == preC09.Id).Status.Should().Be(WaitlistStatus.Expired);
        _db.WaitlistEntries.Single(w => w.Id == nextInLine.Id).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task RunAsync_BookedEntry_IsNeitherExpiredNorCascaded()
    {
        Waitlist booked = await SeedNotified(DateTime.UtcNow.AddHours(-25));
        booked.Status = WaitlistStatus.Booked;
        await _db.SaveChangesAsync(default);
        Waitlist nextInLine = await SeedWaiting(createdAt: DateTime.UtcNow.AddHours(-10));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == booked.Id).Status.Should().Be(WaitlistStatus.Booked);
        _db.WaitlistEntries.Single(w => w.Id == nextInLine.Id).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task RunAsync_SetsEachStudioAsTheTenantBeforeMatching()
    {
        // The match reads waitlist entries through the tenant filter, which with no tenant set matches nothing.
        // The fake context applies no filters, so this asserts the job sets the tenant rather than relying on
        // the fake to show the empty result production got.
        Guid otherStudio = Guid.NewGuid();
        await SeedNotified(DateTime.UtcNow.AddHours(-25));
        await SeedNotified(DateTime.UtcNow.AddHours(-25), studioId: otherStudio);

        await CreateSut().RunAsync();

        _tenant.Received(1).SetTenant(_studioId);
        _tenant.Received(1).SetTenant(otherStudio);
    }

    private async Task<Waitlist> SeedNotified(
        DateTime notifiedAt, DateTime? preferredFrom = null, bool recordSlot = true, Guid? studioId = null)
    {
        Waitlist entry = new()
        {
            StudioId = studioId ?? _studioId,
            ArtistId = _artistId,
            PreferredDateFrom = preferredFrom ?? _slotDate.AddDays(-1),
            PreferredDateTo = _slotDate.AddDays(1),
            Status = WaitlistStatus.Notified,
            NotifiedAt = notifiedAt,
            NotifiedSlotStart = recordSlot ? _slotDate : null,
            NotifiedArtistId = recordSlot ? _artistId : null,
            NotifiedSlotDurationMinutes = recordSlot ? 60 : null,
            GuestName = "Notified Guest",
            GuestEmail = "notified@example.com",
        };
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(default);
        return entry;
    }

    private async Task<Waitlist> SeedWaiting(DateTime createdAt, DateTime? from = null, DateTime? to = null)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            PreferredDateFrom = from ?? _slotDate.AddDays(-1),
            PreferredDateTo = to ?? _slotDate.AddDays(1),
            Status = WaitlistStatus.Waiting,
            GuestName = "Waiting Guest",
            GuestEmail = "waiting@example.com",
            CreatedAt = createdAt,
        };
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(default);
        return entry;
    }
}
