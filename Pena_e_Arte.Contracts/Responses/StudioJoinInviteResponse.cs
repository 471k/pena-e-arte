namespace Pena_e_Arte.Contracts.Responses;

public record StudioJoinInviteResponse(
    Guid Id,
    string InvitedEmail,
    string Status,
    DateTime ExpiresAt);

/// <summary>The invitee's own view of a pending invite — carries the inviting studio's
/// public Name/Slug/City only, nothing not already public.</summary>
public record MyStudioJoinInviteResponse(
    Guid Id,
    Guid StudioId,
    string StudioName,
    string StudioSlug,
    string StudioCity,
    DateTime ExpiresAt);
