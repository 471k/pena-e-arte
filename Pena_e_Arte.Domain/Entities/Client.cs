namespace Pena_e_Arte.Domain.Entities;

public class Client : TenantEntity
{
    public Guid? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>
    /// Set when the client (or support on their behalf) requests account erasure. The account's
    /// login is disabled immediately; the RetentionPurgeJob anonymizes this row's PII after the
    /// grace window (the row itself can't be deleted — appointments/payments FK-reference it).
    /// </summary>
    public DateTime? ErasureRequestedAt { get; set; }

    public ClientProfile? Profile { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<TattooRecord> TattooRecords { get; set; } = [];
}
