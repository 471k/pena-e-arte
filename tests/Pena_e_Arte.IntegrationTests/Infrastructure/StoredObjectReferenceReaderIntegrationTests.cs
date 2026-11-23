using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// The reader on the real, filter-configured context under a job's conditions (no tenant set). FakeDbContext
/// applies no query filters, so only this proves the tenant filter and the soft-delete filter are bypassed:
/// under the filters, every object would look unreferenced and the sweep would delete live files.
/// </summary>
[Collection("Database")]
public class StoredObjectReferenceReaderIntegrationTests(DatabaseFixture fixture)
{
    private const string Address = "https://media.example.com";

    private static R2Service R2() => new(Substitute.For<IAmazonS3>(), Options.Create(new R2Options
    {
        BucketName = "test-bucket",
        PublicUrl = Address,
    }));

    [Fact]
    public async Task UnderJobConditions_FindsReferencesInEveryStudio_IncludingSoftDeletedRows()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        string liveA = $"{studioA}/portfolio/{Guid.NewGuid()}.jpg";
        string softDeletedB = $"{studioB}/clients/c/photos/{Guid.NewGuid()}.jpg";
        string attachmentB = $"{studioB}/appointments/pending/s/Reference/{Guid.NewGuid()}.jpg";

        await using (AppDbContext seedA = fixture.CreateDbContext(studioA))
        {
            Artist artist = new() { StudioId = studioA, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@test.com" };
            seedA.Artists.Add(artist);
            await seedA.SaveChangesAsync();
            seedA.PortfolioImages.Add(new PortfolioImage { StudioId = studioA, ArtistId = artist.Id, ImageUrl = $"{Address}/{liveA}" });
            await seedA.SaveChangesAsync();
        }
        await using (AppDbContext seedB = fixture.CreateDbContext(studioB))
        {
            Client client = new() { StudioId = studioB, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@test.com" };
            Artist artist = new() { StudioId = studioB, FirstName = "E", LastName = "F", Email = $"{Guid.NewGuid()}@test.com" };
            seedB.Clients.Add(client);
            seedB.Artists.Add(artist);
            await seedB.SaveChangesAsync();

            seedB.TattooRecords.Add(new TattooRecord
            {
                StudioId = studioB,
                ClientId = client.Id,
                ArtistId = artist.Id,
                PhotoUrls = [$"{Address}/{softDeletedB}"],
                DeletedAt = DateTime.UtcNow,
            });
            Appointment appointment = new()
            {
                StudioId = studioB,
                ClientId = client.Id,
                ArtistId = artist.Id,
                Date = DateTime.UtcNow.AddDays(3),
                EndDate = DateTime.UtcNow.AddDays(3).AddHours(1),
                DurationMinutes = 60,
            };
            seedB.Appointments.Add(appointment);
            await seedB.SaveChangesAsync();
            seedB.AppointmentAttachments.Add(new AppointmentAttachment
            {
                StudioId = studioB,
                AppointmentId = appointment.Id,
                ImageUrl = $"{Address}/{attachmentB}",
                DeletedAt = DateTime.UtcNow,
            });
            await seedB.SaveChangesAsync();
        }

        // A job's conditions: no tenant set (Guid.Empty), the same context a Hangfire job resolves.
        await using AppDbContext job = fixture.CreateDbContext(Guid.Empty);
        StoredObjectReferences refs = await new StoredObjectReferenceReader(job, R2()).ReadAsync(default);

        refs.IsReferenced(liveA).Should().BeTrue(because: "studio A's live portfolio row references it");
        refs.IsReferenced(softDeletedB).Should().BeTrue(because: "a soft-deleted tattoo record still protects its photo");
        refs.IsReferenced(attachmentB).Should().BeTrue(because: "a soft-deleted attachment still protects its image");
    }

    [Fact]
    public async Task Sweep_UnderJobConditions_NeverCountsAReferencedObjectAsAnOrphan()
    {
        Guid studio = Guid.NewGuid();
        string referenced = $"{studio}/portfolio/{Guid.NewGuid()}.jpg";
        string orphan = $"{studio}/portfolio/{Guid.NewGuid()}.jpg";
        string young = $"{studio}/portfolio/{Guid.NewGuid()}.jpg";
        await using (AppDbContext seed = fixture.CreateDbContext(studio))
        {
            Artist artist = new() { StudioId = studio, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@test.com" };
            seed.Artists.Add(artist);
            await seed.SaveChangesAsync();
            seed.PortfolioImages.Add(new PortfolioImage { StudioId = studio, ArtistId = artist.Id, ImageUrl = $"{Address}/{referenced}" });
            await seed.SaveChangesAsync();
        }

        // The URL methods come from the real R2Service; only the listing is faked.
        R2Service real = R2();
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.IsR2Url(Arg.Any<string>()).Returns(ci => real.IsR2Url(ci.Arg<string>()));
        r2.TryGetObjectKey(Arg.Any<string>()).Returns(ci => real.TryGetObjectKey(ci.Arg<string>()));
        DateTime old = DateTime.UtcNow.AddDays(-30);
        r2.ListAllExceptAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new List<R2ObjectInfo>
        {
            new(referenced, old, 100),
            new(orphan, old, 200),
            new(young, DateTime.UtcNow.AddHours(-1), 300),
        });
        ListLogger<OrphanedObjectSweepJob> logger = new();

        await using AppDbContext job = fixture.CreateDbContext(Guid.Empty);
        await new OrphanedObjectSweepJob(r2, new StoredObjectReferenceReader(job, r2), TimeProvider.System,
            Options.Create(new OrphanedObjectSweepOptions()), logger).RunAsync();

        logger.Messages(Microsoft.Extensions.Logging.LogLevel.Information).Should().Contain(m =>
            m.Contains($"would delete 1 object(s), 200 bytes, owner {studio}, area portfolio"));
        await r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
