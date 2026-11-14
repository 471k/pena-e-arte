using FluentAssertions;
using Pena_e_Arte.Application.Waitlists.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Waitlists;

public class WaitlistMatchExtensionsTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly DateTime _slotDate = BookableSlotSeeder.FutureSlot();
    private const int Duration = 90;

    public WaitlistMatchExtensionsTests() =>
        BookableSlotSeeder.SeedAlwaysBookableArtist(_db, _studioId, _artistId);

    private Task<Guid?> Claim(Guid? freedAppointmentId = null, DateTime? slot = null) =>
        _db.ClaimNextMatchAsync(_studioId, _artistId, slot ?? _slotDate, Duration, freedAppointmentId, default);

    [Fact]
    public async Task ClaimNextMatchAsync_MultipleWaitingEntries_MatchesOldestFirst()
    {
        Waitlist older = await Seed(createdAt: DateTime.UtcNow.AddHours(-5));
        Waitlist newer = await Seed(createdAt: DateTime.UtcNow.AddHours(-1));

        Guid? matchedId = await Claim();

        matchedId.Should().Be(older.Id);
        _db.WaitlistEntries.Single(w => w.Id == older.Id).Status.Should().Be(WaitlistStatus.Notified);
        _db.WaitlistEntries.Single(w => w.Id == newer.Id).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_MatchOnlyNotifiesOneEntry()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-5));
        await Seed(createdAt: DateTime.UtcNow.AddHours(-4));

        await Claim();
        await _db.SaveChangesAsync(default);

        _db.WaitlistEntries.Count(w => w.Status == WaitlistStatus.Notified).Should().Be(1);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_ArtistNullEntry_MatchesAnyArtist()
    {
        Waitlist entry = await Seed(createdAt: DateTime.UtcNow.AddHours(-1), anyArtist: true);

        Guid? matchedId = await Claim();

        matchedId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_WrongArtist_DoesNotMatch()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1), artistId: Guid.NewGuid());

        Guid? matchedId = await Claim();

        matchedId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextMatchAsync_DateOutsidePreferredWindow_DoesNotMatch()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1),
            from: _slotDate.AddDays(10), to: _slotDate.AddDays(20));

        Guid? matchedId = await Claim();

        matchedId.Should().BeNull();
    }

    // ── C09: the entry records which slot it was offered ──────────────────────

    [Fact]
    public async Task ClaimNextMatchAsync_RecordsTheOfferedSlot()
    {
        Waitlist entry = await Seed(createdAt: DateTime.UtcNow.AddHours(-1), anyArtist: true);

        await Claim();

        Waitlist matched = _db.WaitlistEntries.Single(w => w.Id == entry.Id);
        matched.NotifiedAt.Should().NotBeNull();
        matched.NotifiedSlotStart.Should().Be(_slotDate);
        // The freed slot's artist, not the entry's own ArtistId (null here: "any artist").
        matched.NotifiedArtistId.Should().Be(_artistId);
        matched.NotifiedSlotDurationMinutes.Should().Be(Duration);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_NotifiedAtIsWholeSeconds_SoTheClaimTokenSurvivesTheDatabaseRoundTrip()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1));

        await Claim();

        DateTime notifiedAt = _db.WaitlistEntries.Single().NotifiedAt!.Value;
        (notifiedAt.Ticks % TimeSpan.TicksPerSecond).Should().Be(0);
        notifiedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ── C09: a cascade never notifies about a slot it hasn't confirmed is free ──

    [Fact]
    public async Task ClaimNextMatchAsync_SlotAlreadyBookedBySomeoneElse_NotifiesNobody()
    {
        Waitlist entry = await Seed(createdAt: DateTime.UtcNow.AddHours(-1));
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slotDate.AddMinutes(30));

        Guid? matchedId = await Claim();

        matchedId.Should().BeNull();
        _db.WaitlistEntries.Single(w => w.Id == entry.Id).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_TheFreedAppointmentStillInTheDatabase_IsExcluded()
    {
        // The cancel path matches before saving the cancellation: the row still holds the slot.
        Waitlist entry = await Seed(createdAt: DateTime.UtcNow.AddHours(-1));
        Appointment beingCancelled = BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slotDate, Duration);

        Guid? matchedId = await Claim(freedAppointmentId: beingCancelled.Id);

        matchedId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_ExcludingTheFreedAppointment_StillSeesOtherBookings()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1));
        Appointment beingCancelled = BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slotDate, Duration);
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slotDate.AddMinutes(60));

        Guid? matchedId = await Claim(freedAppointmentId: beingCancelled.Id);

        matchedId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextMatchAsync_SlotInThePast_NotifiesNobody()
    {
        DateTime past = DateTime.UtcNow.AddHours(-2);
        await Seed(createdAt: DateTime.UtcNow.AddHours(-5), from: past.AddDays(-1), to: past.AddDays(1));

        Guid? matchedId = await Claim(slot: past);

        matchedId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextMatchAsync_ArtistNotWorkingThen_NotifiesNobody()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1));
        _db.ArtistSchedules.RemoveRange(_db.ArtistSchedules.Where(s => s.DayOfWeek == _slotDate.DayOfWeek));
        await _db.SaveChangesAsync(default);

        Guid? matchedId = await Claim();

        matchedId.Should().BeNull();
    }

    private async Task<Waitlist> Seed(
        DateTime createdAt, Guid? artistId = null, bool anyArtist = false, DateTime? from = null, DateTime? to = null)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = anyArtist ? null : artistId ?? _artistId,
            PreferredDateFrom = from ?? _slotDate.AddDays(-1),
            PreferredDateTo = to ?? _slotDate.AddDays(1),
            Status = WaitlistStatus.Waiting,
            GuestName = "Test Guest",
            GuestEmail = "guest@example.com",
            CreatedAt = createdAt, // init-only, settable in the object initializer
        };
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(default);

        return entry;
    }
}
