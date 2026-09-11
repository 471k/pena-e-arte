namespace Pena_e_Arte.Contracts.Responses;

public record StudioApiKeyStatusResponse(
    bool HasActiveKey,
    string? KeyPrefix,
    DateTime? CreatedAt,
    DateTime? LastUsedAt);
