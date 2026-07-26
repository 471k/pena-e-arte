namespace Pena_e_Arte.Contracts.Responses;

public record InstagramConnectionStatusResponse(
    bool IsConnected,
    string? Username,
    DateTime? LastSyncedAt,
    int PostCount);
