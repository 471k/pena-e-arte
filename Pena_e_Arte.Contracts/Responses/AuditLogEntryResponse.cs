namespace Pena_e_Arte.Contracts.Responses;

/// <summary>StudioName is resolved by the admin read only; the owner's own-studio read leaves it null.</summary>
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
    string? StudioName = null);

public record AuditLogPageResponse(
    List<AuditLogEntryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record AuditLogVocabularyResponse(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> TargetTypes);
