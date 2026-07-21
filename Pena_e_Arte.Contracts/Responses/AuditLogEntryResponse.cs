namespace Pena_e_Arte.Contracts.Responses;

public record AuditLogEntryResponse(
    Guid     Id,
    Guid     ActorUserId,
    string   ActorRole,
    string   Action,
    string   TargetType,
    Guid     TargetId,
    Guid?    StudioId,
    string   Metadata,
    DateTime CreatedAt);

public record AuditLogPageResponse(
    List<AuditLogEntryResponse> Items,
    int                         TotalCount,
    int                         Page,
    int                         PageSize);
