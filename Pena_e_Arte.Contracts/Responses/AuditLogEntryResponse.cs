namespace Pena_e_Arte.Contracts.Responses;

/// <summary>StudioName, ActorName and ActorAccountDeleted are resolved by the admin read only; the owner's own-studio
/// read leaves them at their defaults. ActorName is the account's given name, resolved when the page is read (Phi,
/// 19 Nov: current name, never a stored copy), for staff roles only: a client is never named. ActorAccountDeleted is
/// true when the actor's account no longer exists.</summary>
public record AuditLogEntryResponse(
    Guid Id,
    Guid ActorUserId,
    string ActorRole,
    string Action,
    string TargetType,
    Guid TargetId,
    Guid? StudioId,
    string Metadata,
    DateTime CreatedAt,
    string? StudioName = null,
    string? ActorName = null,
    bool ActorAccountDeleted = false);

public record AuditLogPageResponse(
    List<AuditLogEntryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record AuditLogVocabularyResponse(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> TargetTypes);
