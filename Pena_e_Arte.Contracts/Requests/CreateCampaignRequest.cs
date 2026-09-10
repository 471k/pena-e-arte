namespace Pena_e_Arte.Contracts.Requests;

public record CreateCampaignRequest(
    string Subject,
    string BodyHtml,
    string Audience,                          // CampaignAudience name: AllClients | ClientsWithNoRecentVisit | Custom
    int? NoRecentVisitDays = null,             // only meaningful for ClientsWithNoRecentVisit; defaults to 90
    IReadOnlyList<Guid>? CustomClientIds = null); // only meaningful for Custom
