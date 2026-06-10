using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetIndustryReportsQuery : IRequest<IReadOnlyList<IndustryReportSummaryResponse>>;

public class GetIndustryReportsHandler(IR2Service r2)
    : IRequestHandler<GetIndustryReportsQuery, IReadOnlyList<IndustryReportSummaryResponse>>
{
    private const string ReportPrefix = "reports/industry/";

    public async Task<IReadOnlyList<IndustryReportSummaryResponse>> Handle(
        GetIndustryReportsQuery query, CancellationToken ct)
    {
        IReadOnlyList<R2ObjectInfo> objects = await r2.ListByPrefixAsync(ReportPrefix, ct);

        List<IndustryReportSummaryResponse> reports = new();
        foreach (R2ObjectInfo obj in objects.OrderByDescending(o => o.Key))
        {
            string period      = ExtractPeriod(obj.Key);
            string downloadUrl = await r2.GeneratePresignedReadUrlAsync(obj.Key, TimeSpan.FromHours(24), ct);
            reports.Add(new IndustryReportSummaryResponse(period, obj.LastModified, downloadUrl));
        }

        return reports;
    }

    private static string ExtractPeriod(string key)
    {
        // "reports/industry/2026-06.json" → "2026-06"
        string filename = key[ReportPrefix.Length..];
        return filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? filename[..^5]
            : filename;
    }
}
