using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.ExternalApi.Commands;
using Pena_e_Arte.Application.ExternalApi.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.ExternalApi;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

// The single most important property of this feature: an API key scoped to one studio must
// never be able to see another studio's data. Proven against real MySQL and the real global
// query filter (AppDbContext), not FakeDbContext — the unit tests can't exercise tenant
// isolation at all, since FakeDbContext applies no filter.
[Collection("Database")]
public class ExternalApiIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GenerateApiKey_ThenLookupByHash_ResolvesTheCorrectStudio_NotAnyOther()
    {
        Guid studioAId = Guid.NewGuid();
        Guid studioBId = Guid.NewGuid();
        string rawKey = await SeedStudioWithApiKeyAsync(studioAId, allowApiAccess: true);
        await SeedStudioWithApiKeyAsync(studioBId, allowApiAccess: true);

        // Mirrors ApiKeyAuthenticationHandler's own lookup exactly: hash the raw key, find by
        // hash with IgnoreQueryFilters() (the caller's studio isn't known yet at that point).
        await using AppDbContext lookupDb = fixture.CreateDbContext(Guid.Empty);
        string keyHash = ApiKeyHasher.Hash(rawKey);
        StudioApiKey? resolved = await lookupDb.StudioApiKeys
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.RevokedAt == null);

        resolved.Should().NotBeNull();
        resolved!.StudioId.Should().Be(studioAId);
        resolved.StudioId.Should().NotBe(studioBId);
    }

    // Regression test for a real bug this suite caught: StudioApiKey was missing from
    // AppDbContext's manually-enumerated HasQueryFilter list (every TenantEntity needs an
    // explicit line there — it is not automatic), so GenerateStudioApiKeyHandler's own
    // "revoke whatever is currently active" query ran completely unscoped. Generating a key
    // for studio B silently revoked studio A's already-active key, because the query saw
    // every studio's keys as if they were its own. Only catchable against the real
    // AppDbContext — FakeDbContext applies no filter at all, so the unit tests passed
    // throughout.
    [Fact]
    public async Task GenerateApiKey_ForStudioB_DoesNotRevokeStudioAsExistingKey()
    {
        Guid studioAId = Guid.NewGuid();
        Guid studioBId = Guid.NewGuid();
        string rawKeyA = await SeedStudioWithApiKeyAsync(studioAId, allowApiAccess: true);

        await SeedStudioWithApiKeyAsync(studioBId, allowApiAccess: true);

        await using AppDbContext lookupDb = fixture.CreateDbContext(Guid.Empty);
        StudioApiKey keyA = await lookupDb.StudioApiKeys
            .IgnoreQueryFilters()
            .SingleAsync(k => k.KeyHash == ApiKeyHasher.Hash(rawKeyA));

        keyA.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetExternalAppointments_ScopedToStudioA_NeverReturnsStudioBsAppointments()
    {
        Guid studioAId = Guid.NewGuid();
        Guid studioBId = Guid.NewGuid();
        await SeedStudioWithApiKeyAsync(studioAId, allowApiAccess: true);
        await SeedStudioWithApiKeyAsync(studioBId, allowApiAccess: true);

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        Client clientA = new() { StudioId = studioAId, FirstName = "Ana", LastName = "Silva", Email = $"{Guid.NewGuid():N}@test.com" };
        Client clientB = new() { StudioId = studioBId, FirstName = "Rui", LastName = "Costa", Email = $"{Guid.NewGuid():N}@test.com" };
        Artist artistA = new() { StudioId = studioAId, FirstName = "Elena", LastName = "Martins", Email = $"{Guid.NewGuid():N}@test.com" };
        Artist artistB = new() { StudioId = studioBId, FirstName = "Marco", LastName = "Santos", Email = $"{Guid.NewGuid():N}@test.com" };
        seedDb.Clients.AddRange(clientA, clientB);
        seedDb.Artists.AddRange(artistA, artistB);
        await seedDb.SaveChangesAsync();

        seedDb.Appointments.AddRange(
            new Appointment
            {
                StudioId = studioAId,
                ArtistId = artistA.Id,
                ClientId = clientA.Id,
                Date = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
                DurationMinutes = 60,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
            },
            new Appointment
            {
                StudioId = studioBId,
                ArtistId = artistB.Id,
                ClientId = clientB.Id,
                Date = DateTime.UtcNow.AddDays(2),
                EndDate = DateTime.UtcNow.AddDays(2).AddHours(1),
                DurationMinutes = 60,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
            });
        await seedDb.SaveChangesAsync();

        await using AppDbContext studioADb = fixture.CreateDbContext(studioAId);
        GetExternalAppointmentsHandler handler = new(studioADb);

        List<ExternalAppointmentResponse> result =
            await handler.Handle(new GetExternalAppointmentsQuery(), default);

        result.Should().ContainSingle();
        result[0].ClientName.Should().Be("Ana Silva");
    }

    [Fact]
    public async Task GetExternalClients_ScopedToStudioA_NeverReturnsStudioBsClients()
    {
        Guid studioAId = Guid.NewGuid();
        Guid studioBId = Guid.NewGuid();

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        seedDb.Clients.AddRange(
            new Client { StudioId = studioAId, FirstName = "Ana", LastName = "Silva", Email = $"{Guid.NewGuid():N}@test.com" },
            new Client { StudioId = studioBId, FirstName = "Rui", LastName = "Costa", Email = $"{Guid.NewGuid():N}@test.com" });
        await seedDb.SaveChangesAsync();

        await using AppDbContext studioADb = fixture.CreateDbContext(studioAId);
        GetExternalClientsHandler handler = new(studioADb);

        List<ExternalClientResponse> result = await handler.Handle(new GetExternalClientsQuery(), default);

        result.Should().ContainSingle();
        result[0].FirstName.Should().Be("Ana");
    }

    [Fact]
    public async Task GenerateApiKey_StudioOnFreePlan_ThrowsBusinessRuleViolationException()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudioWithApiKeyAsync(studioId, allowApiAccess: false, generateKey: false);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        GenerateStudioApiKeyHandler handler = new(db, new StubCurrentTenant(studioId));

        Func<Task> act = () => handler.Handle(new GenerateStudioApiKeyCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    private async Task<string> SeedStudioWithApiKeyAsync(Guid studioId, bool allowApiAccess, bool generateKey = true)
    {
        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        Plan plan = new() { Name = $"Plan-{Guid.NewGuid():N}", AllowApiAccess = allowApiAccess };
        seedDb.Plans.Add(plan);
        seedDb.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Test Studio",
            Slug = $"studio-{Guid.NewGuid():N}",
            City = "Lisbon",
        });
        seedDb.Subscriptions.Add(new Subscription { StudioId = studioId, PlanId = plan.Id });
        await seedDb.SaveChangesAsync();

        if (!generateKey) return string.Empty;

        await using AppDbContext genDb = fixture.CreateDbContext(studioId);
        GenerateStudioApiKeyHandler handler = new(genDb, new StubCurrentTenant(studioId));
        GenerateApiKeyResponse result = await handler.Handle(new GenerateStudioApiKeyCommand(), default);
        return result.ApiKey;
    }

    private sealed class StubCurrentTenant(Guid studioId) : Domain.Interfaces.ICurrentTenant
    {
        public Guid StudioId { get; private set; } = studioId;
        public bool IsSet => true;
        public void SetTenant(Guid studioId) => StudioId = studioId;
    }
}
