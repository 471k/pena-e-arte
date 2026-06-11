namespace Pena_e_Arte.Contracts.Responses;

public record IndustryReportSummaryResponse(
    string   Period,
    DateTime GeneratedAt,
    string   DownloadUrl
);
