using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// D08 against real MySQL: the admin studio list (GET /api/v1/studios) costs one statement however
/// many studios there are. It used to run StudioCurrencyLock.IsLockedAsync per row (four EXISTS
/// each), which measured over one second at about 20 studios on staging (25 Nov 2026). The list
/// therefore does not compute CurrencyLocked; the single-studio reads still do.
/// </summary>
[Collection("Database")]
public class StudioListIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task TheListQueryCount_DoesNotScaleWithTheNumberOfStudios()
    {
        await SeedStudios(2);
        (int forFew, List<StudioResponse> fewResult) = await CountListQueries();

        await SeedStudios(15);
        (int forMany, List<StudioResponse> manyResult) = await CountListQueries();

        manyResult.Count.Should().Be(fewResult.Count + 15);
        forFew.Should().Be(1, "the list is one SELECT over studios; a per-row query shows up here");
        forMany.Should().Be(forFew, "the list must never issue a query per studio (D08)");
    }

    [Fact]
    public async Task TheList_DoesNotComputeTheCurrencyLock_TheDetailReadStillDoes()
    {
        Guid studioId = (await SeedStudios(1)).Single();
        await using (AppDbContext ctx = fixture.CreateDbContext(studioId))
        {
            ctx.GiftCards.Add(new GiftCard
            {
                StudioId = studioId,
                Code = $"D08{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                InitialBalance = 10m,
                RemainingBalance = 10m,
                Currency = "EUR",
                PurchaserEmail = "buyer@example.invalid",
                Status = GiftCardStatus.Active,
                Provider = "pok",
            });
            await ctx.SaveChangesAsync();
        }

        (_, List<StudioResponse> list) = await CountListQueries();
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        StudioResponse detail = await new GetStudioByIdHandler(db).Handle(new GetStudioByIdQuery(studioId), default);

        detail.CurrencyLocked.Should().BeTrue("money has moved in this studio, and the detail read computes the lock");
        list.Single(s => s.Id == studioId).CurrencyLocked.Should().BeFalse(
            "the list does NOT compute the lock (D08), so its false means 'not computed', not 'not locked'. "
            + "If this fails because the list now reports true, check it is computed set-based in one query: "
            + "the other test in this class pins the statement count");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<(int Count, List<StudioResponse> Result)> CountListQueries()
    {
        CommandCounter counter = new();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, DatabaseServerVersion.MySql)
            .AddInterceptors(counter)
            .Options;
        await using AppDbContext db = new(options, new CurrentTenantService());

        List<StudioResponse> result = await new GetStudiosHandler(db).Handle(new GetStudiosQuery(), default);
        return (counter.Count, result);
    }

    private async Task<List<Guid>> SeedStudios(int count)
    {
        List<Guid> ids = [];
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);
        for (int i = 0; i < count; i++)
        {
            Guid id = Guid.NewGuid();
            ids.Add(id);
            ctx.Studios.Add(new Studio
            {
                Id = id,
                Name = $"D08 {id:N}"[..12],
                Slug = $"d08-{id:N}"[..12],
                City = "Tirana",
                CountryCode = "AL",
                Currency = "EUR",
                IsActive = true,
            });
        }
        await ctx.SaveChangesAsync();
        return ids;
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }
}
