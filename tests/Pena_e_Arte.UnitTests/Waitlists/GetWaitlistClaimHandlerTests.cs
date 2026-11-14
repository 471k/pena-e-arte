using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Waitlists.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.Waitlists;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Waitlists;

public class GetWaitlistClaimHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly WaitlistClaimTokens _tokens = new(new EphemeralDataProtectionProvider());
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly DateTime _slot = BookableSlotSeeder.FutureSlot();

    public GetWaitlistClaimHandlerTests()
    {
        BookableSlotSeeder.SeedAlwaysBookableArtist(_db, _studioId, _artistId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink Test", Slug = "ink-test", Timezone = "UTC" });
        _db.SaveChanges();
    }

    private GetWaitlistClaimHandler CreateSut() =>
        new(_db, _tenant, _tokens, NullLogger<GetWaitlistClaimHandler>.Instance);

    private Task<WaitlistClaimResponse> Read(string token) =>
        CreateSut().Handle(new GetWaitlistClaimQuery(token), default);

    [Fact]
    public async Task OpenOfferForAFreeSlot_IsClaimable_WithWhatThePageNeeds()
    {
        Waitlist entry = SeedNotified(DateTime.UtcNow.AddHours(-1));

        WaitlistClaimResponse result = await Read(_tokens.Mint(entry));

        result.State.Should().Be(WaitlistClaimStates.Claimable);
        result.StudioSlug.Should().Be("ink-test");
        result.StudioName.Should().Be("Ink Test");
        result.ArtistId.Should().Be(_artistId);
        result.ArtistName.Should().Be("Bookable Artist");
        result.SlotStart.Should().Be(_slot);
        result.DurationMinutes.Should().Be(120);
        result.WindowEndsAt.Should().Be(entry.NotifiedAt!.Value.AddHours(24));
        _tenant.Received().SetTenant(_studioId);
    }

    [Fact]
    public async Task SomeoneBookedTheSlotSince_IsSlotTaken()
    {
        Waitlist entry = SeedNotified(DateTime.UtcNow.AddHours(-1));
        BookableSlotSeeder.SeedAppointment(_db, _studioId, _artistId, _slot.AddMinutes(30));

        (await Read(_tokens.Mint(entry))).State.Should().Be(WaitlistClaimStates.SlotTaken);
    }

    [Fact]
    public async Task NotifiedButPastTheWindow_IsWindowEnded_EvenBeforeTheJobRuns()
    {
        Waitlist entry = SeedNotified(DateTime.UtcNow.AddHours(-25));

        (await Read(_tokens.Mint(entry))).State.Should().Be(WaitlistClaimStates.WindowEnded);
    }

    [Theory]
    [InlineData(WaitlistStatus.Booked, WaitlistClaimStates.Booked)]
    [InlineData(WaitlistStatus.Expired, WaitlistClaimStates.WindowEnded)]
    [InlineData(WaitlistStatus.Cancelled, WaitlistClaimStates.Cancelled)]
    public async Task ClosedOffers_SayWhy(WaitlistStatus status, string expectedState)
    {
        Waitlist entry = SeedNotified(DateTime.UtcNow.AddHours(-1));
        string token = _tokens.Mint(entry);
        entry.Status = status;
        await _db.SaveChangesAsync(default);

        (await Read(token)).State.Should().Be(expectedState);
    }

    [Fact]
    public async Task Reading_ChangesNothing()
    {
        // Email link scanners open links before people do.
        Waitlist entry = SeedNotified(DateTime.UtcNow.AddHours(-1));
        string token = _tokens.Mint(entry);

        await Read(token);
        await Read(token);

        Waitlist after = _db.WaitlistEntries.Single(w => w.Id == entry.Id);
        after.Status.Should().Be(WaitlistStatus.Notified);
        (await Read(token)).State.Should().Be(WaitlistClaimStates.Claimable);
    }

    // ── One identical 404 for anything that isn't a valid, current token ──

    [Fact]
    public async Task EveryInvalidCase_IsTheSame404()
    {
        List<string> messages = [];

        async Task Capture(string token)
        {
            Func<Task> act = () => Read(token);
            NotFoundException ex = (await act.Should().ThrowAsync<NotFoundException>()).Which;
            messages.Add(ex.Message);
        }

        // 1. Doesn't verify.
        await Capture("not-a-real-token");

        // 2. The entry is gone.
        Waitlist gone = SeedNotified(DateTime.UtcNow.AddHours(-1));
        string goneToken = _tokens.Mint(gone);
        _db.WaitlistEntries.Remove(gone);
        await _db.SaveChangesAsync(default);
        await Capture(goneToken);

        // 3. A token from an earlier notification of the same entry.
        Waitlist renotified = SeedNotified(DateTime.UtcNow.AddHours(-1));
        string oldToken = _tokens.Mint(renotified);
        renotified.NotifiedAt = renotified.NotifiedAt!.Value.AddMinutes(5);
        await _db.SaveChangesAsync(default);
        await Capture(oldToken);

        // 4. A row with no recorded slot (notified before C09).
        Waitlist noSlot = SeedNotified(DateTime.UtcNow.AddHours(-1), recordSlot: false);
        await Capture(_tokens.Mint(noSlot));

        // 5. Studio mismatch (only possible with a forged payload; checked anyway).
        Waitlist moved = SeedNotified(DateTime.UtcNow.AddHours(-1));
        string movedToken = _tokens.Mint(moved);
        moved.StudioId = Guid.NewGuid();
        await _db.SaveChangesAsync(default);
        await Capture(movedToken);

        messages.Should().HaveCount(5);
        messages.Distinct().Should().ContainSingle("no failure may be told apart from another");
        messages[0].Should().NotContainAny(gone.Id.ToString(), renotified.Id.ToString(), noSlot.Id.ToString());
    }

    [Fact]
    public void Response_CarriesNoPersonalData()
    {
        string[] fields = typeof(WaitlistClaimResponse).GetProperties().Select(p => p.Name).ToArray();

        fields.Should().BeEquivalentTo(
            "State", "StudioSlug", "StudioName", "StudioTimezone", "ArtistId", "ArtistName",
            "SlotStart", "DurationMinutes", "WindowEndsAt");
    }

    private Waitlist SeedNotified(DateTime notifiedAt, bool recordSlot = true)
    {
        Waitlist entry = new()
        {
            StudioId = _studioId,
            ArtistId = null,
            PreferredDateFrom = _slot.AddDays(-1),
            PreferredDateTo = _slot.AddDays(1),
            Status = WaitlistStatus.Notified,
            NotifiedAt = new DateTime(notifiedAt.Ticks - (notifiedAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            NotifiedSlotStart = recordSlot ? _slot : null,
            NotifiedArtistId = recordSlot ? _artistId : null,
            NotifiedSlotDurationMinutes = recordSlot ? 120 : null,
            GuestName = "Private Guest",
            GuestEmail = "private@example.com",
        };
        _db.WaitlistEntries.Add(entry);
        _db.SaveChanges();
        return entry;
    }
}
