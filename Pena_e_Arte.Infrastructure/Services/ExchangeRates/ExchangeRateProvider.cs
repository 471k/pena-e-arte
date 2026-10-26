using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.ExchangeRates;

/// <summary>
/// Reads the newest stored rate straight from the database: one indexed row, so no server-side cache.
/// The public endpoint that serves it to browsers sets Cache-Control: public, max-age=600 instead.
/// </summary>
public class ExchangeRateProvider(IAppDbContext db, TimeProvider time) : IExchangeRateProvider
{
    public async Task<ExchangeRateSnapshot?> GetLatestAsync(string baseCurrency, string quoteCurrency, CancellationToken ct)
    {
        ExchangeRate? latest = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct);

        if (latest is null) return null;

        DateTime nowUtc = time.GetUtcNow().UtcDateTime;
        DateOnly todayTirane = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, BankOfAlbaniaRateParser.Tirane));
        bool isStale = latest.RateDate.AddDays(ExchangeRateSnapshot.StaleAfterDays) < todayTirane;

        return new ExchangeRateSnapshot(latest.Rate, latest.RateDate, latest.Source, isStale);
    }
}
