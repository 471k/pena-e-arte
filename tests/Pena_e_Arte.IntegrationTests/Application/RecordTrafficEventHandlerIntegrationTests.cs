using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Traffic.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

// The unit-test suite (RecordTrafficEventHandlerTests) uses FakeDbContext, which registers no
// HasQueryFilter() calls at all — so it cannot prove IgnoreQueryFilters() is actually load-
// bearing for the anonymous /artist/{slug} lookup (approved usage #41). This test runs against
// the real AppDbContext, where Artist genuinely is tenant-filtered, with the handler's own
// context built for no tenant (Guid.Empty, matching an anonymous beacon caller) — proving the
// cross-tenant lookup would return nothing without IgnoreQueryFilters().
[Collection("Database")]
public class RecordTrafficEventHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Handle_AnonymousArtistSlugBeacon_ResolvesStudioIdAcrossTenants()
    {
        Guid studioId = Guid.NewGuid();
        await using (AppDbContext seedDb = fixture.CreateDbContext(studioId))
        {
            seedDb.Studios.Add(new Studio { Id = studioId, Name = "Ink Society", Slug = $"ink-{studioId:N}", IsActive = true });

            Artist artist = new()
            {
                StudioId = studioId,
                FirstName = "Elena",
                LastName = "Martins",
                Email = "elena@test.com",
                IsActive = true,
            };
            artist.SetSlug($"elena-{Guid.NewGuid():N}");
            seedDb.Artists.Add(artist);
            await seedDb.SaveChangesAsync();

            await using AppDbContext anonymousDb = fixture.CreateDbContext(Guid.Empty);
            RecordTrafficEventHandler handler = new(anonymousDb);
            var command = new RecordTrafficEventCommand(
                Guid.NewGuid(), null, null, null, $"/artist/{artist.Slug}",
                Geo: null, IpHash: null, DeviceType: null, Browser: null, Os: null);

            await handler.Handle(command, default);

            // TrafficEvent itself has no query filter registered (non-tenant shape) — a plain
            // read is correct here, no IgnoreQueryFilters() needed for this table.
            TrafficEvent saved = await anonymousDb.TrafficEvents
                .SingleAsync(t => t.Path == $"/artist/{artist.Slug}");
            saved.StudioId.Should().Be(studioId);
        }
    }
}
