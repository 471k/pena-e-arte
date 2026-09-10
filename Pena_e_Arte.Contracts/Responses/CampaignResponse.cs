namespace Pena_e_Arte.Contracts.Responses;

public record CampaignResponse(
    Guid Id,
    string Subject,
    string BodyHtml,
    string Audience,
    string Status,
    int? NoRecentVisitDays,
    IReadOnlyList<Guid> CustomClientIds,
    DateTime? SentAt,
    int RecipientCount,
    int DeliveredCount,
    DateTime CreatedAt);
