using System.Reflection;
using FluentAssertions;
using Pena_e_Arte.Application.Traffic.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Traffic;

public class RecordTrafficEventHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private RecordTrafficEventHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_StudioIdAlreadyKnownFromJwt_UsesItDirectlyWithoutLookup()
    {
        Guid studioId = Guid.NewGuid();
        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), Guid.NewGuid(), "owner", studioId, "/dashboard",
            Geo: null, IpHash: null, DeviceType: "desktop", Browser: "Chrome", Os: "Windows");

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().Be(studioId);
    }

    [Fact]
    public async Task Handle_AnonymousStudioSlugPath_ResolvesStudioIdViaPlainStudiosQuery()
    {
        Studio studio = new() { Name = "Ink Society", Slug = "ink-society", City = "Tirana" };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/s/ink-society",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().Be(studio.Id);
    }

    [Fact]
    public async Task Handle_AnonymousArtistSlugPath_ResolvesStudioIdViaArtistLookup()
    {
        Studio studio = new() { Name = "Ink Society", Slug = "ink-society", City = "Tirana", IsActive = true };
        _db.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Elena",
            LastName = "Martins",
            Email = "elena@test.com",
            IsActive = true,
        };
        artist.SetSlug("elena-martins");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/artist/elena-martins",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().Be(studio.Id);
    }

    [Fact]
    public async Task Handle_AnonymousArtistSlugPath_StudioDeactivated_LeavesStudioIdNull()
    {
        Studio studio = new() { Name = "Closed Studio", Slug = "closed-studio", City = "Tirana", IsActive = false };
        _db.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Elena",
            LastName = "Martins",
            Email = "elena2@test.com",
            IsActive = true,
        };
        artist.SetSlug("elena-martins-2");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/artist/elena-martins-2",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnonymousStudioSlugPath_StudioDeactivated_LeavesStudioIdNull()
    {
        Studio studio = new() { Name = "Closed Studio", Slug = "closed-studio-2", City = "Tirana", IsActive = false };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/s/closed-studio-2",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownSlug_LeavesStudioIdNull()
    {
        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/s/does-not-exist",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NonSlugPath_LeavesStudioIdNull()
    {
        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, "/discover",
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PathOverTwoHundredChars_IsTruncated()
    {
        string longPath = "/" + new string('a', 250);
        var command = new RecordTrafficEventCommand(
            Guid.NewGuid(), null, null, null, longPath,
            Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

        await CreateSut().Handle(command, default);

        TrafficEvent saved = _db.TrafficEvents.Single();
        saved.Path.Length.Should().Be(200);
    }

    [Fact]
    public void TrafficEvent_HasNoIpAddressProperty_StructurallyEnforcedNotJustRuntime()
    {
        // A raw IP address must never be persisted (CLAUDE.md rule #3 / architecture.md §3.2).
        // Asserted via reflection so this fails loudly if anyone ever adds an IpAddress-shaped
        // property back to the entity, rather than relying on a human noticing in review.
        PropertyInfo[] properties = typeof(TrafficEvent).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        properties.Should().NotContain(p =>
            p.Name.Contains("IpAddress", StringComparison.OrdinalIgnoreCase) ||
            (p.Name.Contains("Ip", StringComparison.OrdinalIgnoreCase) && p.Name != nameof(TrafficEvent.IpHash)));
    }
}
