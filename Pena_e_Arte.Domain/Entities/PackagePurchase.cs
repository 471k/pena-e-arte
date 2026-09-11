namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// No Status enum: ConfirmedAt (null until the provider hold succeeds) is the confirmation flag.
/// The row is created at purchase time (to hold ProviderReferenceId/ClientToken while the client
/// completes payment) with SessionsRemaining = 0 and ConfirmedAt = null;
/// PackagePurchaseReconciliationJob sets ConfirmedAt and SessionsRemaining = Package.SessionCount
/// once the provider confirms payment. A plain nullable timestamp rather than
/// "SessionsRemaining == 0 means unconfirmed" (the literal backlog-spec schema) — that sentinel is
/// ambiguous the moment a real, confirmed purchase is later exhausted by normal use, which would
/// silently re-trigger reconciliation and refill sessions the client already used. No
/// ExpiresAt/refund tracking — packages never expire and are non-refundable once purchased
/// (product decision, 2026-09-09).
/// </summary>
public class PackagePurchase : TenantEntity
{
    public Guid PackageId { get; set; }
    public Guid ClientId { get; set; }
    public int SessionsRemaining { get; set; }
    public string ProviderReferenceId { get; set; } = string.Empty;
    public string? ClientToken { get; set; }
    public string Provider { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }

    public Package Package { get; set; } = null!;
    public Client Client { get; set; } = null!;
}
