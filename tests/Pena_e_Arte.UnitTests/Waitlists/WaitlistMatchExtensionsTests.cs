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
    private readonly DateTime _slotDate = DateTime.UtcNow.AddDays(3);

    [Fact]
    public async Task ClaimNextMatchAsync_MultipleWaitingEntries_MatchesOldestFirst()
    {
        Waitlist older = await Seed(createdAt: DateTime.UtcNow.AddHours(-5));
        Waitlist newer = await Seed(createdAt: DateTime.UtcNow.AddHours(-1));

        Guid? matchedId = await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);

        matchedId.Should().Be(older.Id);
        _db.WaitlistEntries.Single(w => w.Id == older.Id).Status.Should().Be(WaitlistStatus.Notified);
        _db.WaitlistEntries.Single(w => w.Id == newer.Id).Status.Should().Be(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_MatchOnlyNotifiesOneEntry()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-5));
        await Seed(createdAt: DateTime.UtcNow.AddHours(-4));

        await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);
        await _db.SaveChangesAsync(default);

        _db.WaitlistEntries.Count(w => w.Status == WaitlistStatus.Notified).Should().Be(1);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_ArtistNullEntry_MatchesAnyArtist()
    {
        Waitlist entry = await Seed(createdAt: DateTime.UtcNow.AddHours(-1), artistId: null);

        Guid? matchedId = await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);

        matchedId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ClaimNextMatchAsync_WrongArtist_DoesNotMatch()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1), artistId: Guid.NewGuid());

        Guid? matchedId = await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);

        matchedId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextMatchAsync_DateOutsidePreferredWindow_DoesNotMatch()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1),
            from: _slotDate.AddDays(10), to: _slotDate.AddDays(20));

        Guid? matchedId = await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);

        matchedId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextMatchAsync_SetsNotifiedAt()
    {
        await Seed(createdAt: DateTime.UtcNow.AddHours(-1));

        await _db.ClaimNextMatchAsync(_studioId, _artistId, _slotDate, default);

        _db.WaitlistEntries.Single().NotifiedAt.Should().NotBeNull();
    }

    private async Task<Waitlist> Seed(
        DateTime createdAt, Guid? artistId = null, DateTime? from = null, DateTime? to = null)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = artistId ?? _artistId,
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
