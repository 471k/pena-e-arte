namespace Pena_e_Arte.Contracts.Responses.Public;

public record SharedDesignResponse(
    string ImageUrl,
    string Title,
    string StudioName,
    string StudioSlug,
    DateTime ExpiresAt);
