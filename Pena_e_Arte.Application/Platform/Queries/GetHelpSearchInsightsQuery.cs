using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetHelpSearchInsightsQuery(int Days = 30) : IRequest<HelpSearchInsightsResponse>;

public class GetHelpSearchInsightsHandler(IAppDbContext db)
    : IRequestHandler<GetHelpSearchInsightsQuery, HelpSearchInsightsResponse>
{
    public async Task<HelpSearchInsightsResponse> Handle(GetHelpSearchInsightsQuery query, CancellationToken ct)
    {
        DateTime since = DateTime.UtcNow.AddDays(-query.Days);

        // IgnoreQueryFilters approved: usage #39 — cross-tenant aggregate of help search
        // queries for the issuer product-insights view. See architecture.md.
        List<HelpSearchLog> logs = await db.HelpSearchLogs
            .IgnoreQueryFilters()
            .Where(h => h.CreatedAt >= since)
            .ToListAsync(ct);

        List<HelpQueryFrequency> topQueries = logs
            .GroupBy(h => h.Query)
            .Select(g => new HelpQueryFrequency(
                g.Key,
                g.Count(),
                g.Select(h => h.Role).Distinct().OrderBy(r => r).ToArray()))
            .OrderByDescending(f => f.Count)
            .Take(20)
            .ToList();

        List<HelpQueryFrequency> zeroResultQueries = logs
            .Where(h => h.ResultCount == 0)
            .GroupBy(h => h.Query)
            .Select(g => new HelpQueryFrequency(
                g.Key,
                g.Count(),
                g.Select(h => h.Role).Distinct().OrderBy(r => r).ToArray()))
            .OrderByDescending(f => f.Count)
            .ToList();

        return new HelpSearchInsightsResponse(
            TotalSearches:     logs.Count,
            Days:              query.Days,
            TopQueries:        topQueries,
            ZeroResultQueries: zeroResultQueries);
    }
}
