using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class WaitlistNotificationExpiryJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly DateTime _slotDate = DateTime.UtcNow.AddDays(3);

    private WaitlistNotificationExpiryJob CreateSut() =>
        new(_db, _sender, NullLogger<WaitlistNotificationExpiryJob>.Instance);

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
        Waitlist expired = await SeedNotified(DateTime.UtcNow.AddHours(-25));
        Waitlist nextInLine = await SeedWaiting(createdAt: DateTime.UtcNow.AddHours(-10));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Single(w => w.Id == nextInLine.Id).Status.Should().Be(WaitlistStatus.Notified);
    }

    [Fact]
    public async Task RunAsync_ExpiredEntryWithNoOneElseWaiting_NoCascade()
    {
        await SeedNotified(DateTime.UtcNow.AddHours(-25));

        await CreateSut().RunAsync();

        _db.WaitlistEntries.Count(w => w.Status == WaitlistStatus.Notified).Should().Be(0);
    }

    private async Task<Waitlist> SeedNotified(DateTime notifiedAt)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            PreferredDateFrom = _slotDate.AddDays(-1),
            PreferredDateTo = _slotDate.AddDays(1),
            Status = WaitlistStatus.Notified,
            NotifiedAt = notifiedAt,
            GuestName = "Notified Guest",
            GuestEmail = "notified@example.com",
        };
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync(default);
        return entry;
    }

    private async Task<Waitlist> SeedWaiting(DateTime createdAt)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            PreferredDateFrom = _slotDate.AddDays(-1),
            PreferredDateTo = _slotDate.AddDays(1),
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
