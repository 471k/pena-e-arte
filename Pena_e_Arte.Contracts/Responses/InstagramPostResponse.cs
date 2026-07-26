namespace Pena_e_Arte.Contracts.Responses;

public record InstagramPostResponse(
    Guid Id,
    string InstagramMediaId,
    string? MediaUrl,
    string? ThumbnailUrl,
    string? Caption,
    string MediaType,
    DateTime PostedAt,
    bool IsVisible);
