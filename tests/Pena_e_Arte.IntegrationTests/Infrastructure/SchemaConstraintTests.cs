using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class SchemaConstraintTests(DatabaseFixture fixture)
{
    // ── Client email unique per tenant ─────────────────────────────────────────

    [Fact]
    public async Task Client_DuplicateEmailSameTenant_ThrowsDbUpdateException()
    {
        Guid tenantId = Guid.NewGuid();
        string email = $"{Guid.NewGuid()}@example.com";

        await using AppDbContext ctx1 = fixture.CreateDbContext(tenantId);
        ctx1.Clients.Add(new Client { StudioId = tenantId, FirstName = "A", LastName = "B", Email = email });
        await ctx1.SaveChangesAsync();

        await using AppDbContext ctx2 = fixture.CreateDbContext(tenantId);
        ctx2.Clients.Add(new Client { StudioId = tenantId, FirstName = "C", LastName = "D", Email = email });

        Func<Task> act = () => ctx2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Client_SameEmailDifferentTenants_BothSucceed()
    {
        string sharedEmail = $"{Guid.NewGuid()}@shared.com";
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using AppDbContext ctxA = fixture.CreateDbContext(tenantA);
        ctxA.Clients.Add(new Client { StudioId = tenantA, FirstName = "A", LastName = "B", Email = sharedEmail });
        await ctxA.SaveChangesAsync();

        await using AppDbContext ctxB = fixture.CreateDbContext(tenantB);
        ctxB.Clients.Add(new Client { StudioId = tenantB, FirstName = "C", LastName = "D", Email = sharedEmail });

        Func<Task> act = () => ctxB.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Studio slug globally unique ────────────────────────────────────────────

    [Fact]
    public async Task Studio_DuplicateSlug_ThrowsDbUpdateException()
    {
        string slug = $"studio-{Guid.NewGuid():N}".Substring(0, 30);

        await using AppDbContext ctx1 = fixture.CreateDbContext(Guid.Empty);
        ctx1.Studios.Add(new Studio { Name = "Studio One", Slug = slug, City = "Lisbon" });
        await ctx1.SaveChangesAsync();

        await using AppDbContext ctx2 = fixture.CreateDbContext(Guid.Empty);
        ctx2.Studios.Add(new Studio { Name = "Studio Two", Slug = slug, City = "Porto" });

        Func<Task> act = () => ctx2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Studio_UniqueSlug_Succeeds()
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        ctx.Studios.Add(new Studio
        {
            Name = "Unique Studio",
            Slug = $"unique-{Guid.NewGuid():N}".Substring(0, 30),
            City = "Faro"
        });

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── DesignRevision version unique per design ───────────────────────────────

    [Fact]
    public async Task DesignRevision_DuplicateVersionSameDesign_ThrowsDbUpdateException()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);

        await using AppDbContext ctx1 = fixture.CreateDbContext(tenantId);
        ctx1.DesignRevisions.Add(new Domain.Entities.DesignRevision
        {
            StudioId      = tenantId,
            DesignId      = designId,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow
        });
        await ctx1.SaveChangesAsync();

        await using AppDbContext ctx2 = fixture.CreateDbContext(tenantId);
        ctx2.DesignRevisions.Add(new Domain.Entities.DesignRevision
        {
            StudioId      = tenantId,
            DesignId      = designId,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1-dupe.png",
            UploadedAt    = DateTime.UtcNow
        });

        Func<Task> act = () => ctx2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DesignRevision_DifferentVersionsSameDesign_BothSucceed()
    {
        Guid tenantId = Guid.NewGuid();
        Guid designId = await SeedDesign(tenantId);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.DesignRevisions.Add(new Domain.Entities.DesignRevision
        {
            StudioId      = tenantId,
            DesignId      = designId,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow
        });
        ctx.DesignRevisions.Add(new Domain.Entities.DesignRevision
        {
            StudioId      = tenantId,
            DesignId      = designId,
            VersionNumber = 2,
            FileUrl       = "https://r2.example.com/v2.png",
            UploadedAt    = DateTime.UtcNow
        });

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Subscription one-per-studio ────────────────────────────────────────────

    [Fact]
    public async Task Subscription_DuplicateForSameStudio_ThrowsDbUpdateException()
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new() { Name = "Sub Test", Slug = $"sub-{Guid.NewGuid():N}".Substring(0, 20), City = "Lisbon" };
        ctx.Studios.Add(studio);
        await ctx.SaveChangesAsync();

        ctx.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        });
        await ctx.SaveChangesAsync();

        await using AppDbContext ctx2 = fixture.CreateDbContext(Guid.Empty);
        ctx2.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(21),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14)
        });

        Func<Task> act = () => ctx2.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Appointment FK: artist and client must exist ───────────────────────────

    [Fact]
    public async Task Appointment_WithNonExistentArtist_ThrowsDbUpdateException()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        ctx.Appointments.Add(new Appointment
        {
            StudioId        = tenantId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedDesign(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        Design design = new() { StudioId = tenantId, ClientId = client.Id, ArtistId = artist.Id, Title = "Test" };
        ctx.Designs.Add(design);
        await ctx.SaveChangesAsync();
        return design.Id;
    }
}
