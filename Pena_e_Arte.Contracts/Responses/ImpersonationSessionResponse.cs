namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Returned by StartImpersonationCommand — the frontend swaps its active token
/// for AccessToken and stashes the admin's own token aside so "End session" can restore
/// it (see authSlice.ts's startImpersonation/endImpersonation reducers).</summary>
public record ImpersonationTokenResponse(
    Guid SessionId,
    string AccessToken,
    DateTime ExpiresAt,
    Guid StudioId,
    string StudioName);

public record ImpersonationSessionResponse(
    Guid Id,
    Guid ActorUserId,
    Guid StudioId,
    string ReasonCode,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? EndedAt);

public record ImpersonationSessionPageResponse(
    List<ImpersonationSessionResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
