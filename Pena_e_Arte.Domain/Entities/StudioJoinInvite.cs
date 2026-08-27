namespace Pena_e_Arte.Domain.Entities;

public enum StudioJoinInviteStatus { Pending, Accepted, Declined, Expired }

/// <summary>
/// An existing studio inviting a solo/independent artist (a different studio's owner) to
/// dissolve their solo studio and join as an artist here instead. Deliberately not a
/// TenantEntity: the invited party is not a member of the inviting studio's tenant until
/// they accept, so this must be readable/writable across both tenants — see AppDbContext's
/// "Issuer-level (no tenant filter)" section.
/// </summary>
public class StudioJoinInvite
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StudioId { get; set; }
    public string InvitedEmail { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Specializations { get; set; }
    public decimal? HourlyRate { get; set; }
    public StudioJoinInviteStatus Status { get; set; } = StudioJoinInviteStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public Studio Studio { get; set; } = null!;
}
