namespace Pena_e_Arte.Contracts.Responses;

public record DesignShareTokenResponse(
    Guid     Id,
    string   Token,
    string   ShareUrl,
    DateTime ExpiresAt);
