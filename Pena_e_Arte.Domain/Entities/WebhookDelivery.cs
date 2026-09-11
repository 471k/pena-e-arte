namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One row per delivery attempt (not per event) — Hangfire's own automatic retry means
/// a single event can produce several rows here as it's retried, which is deliberate:
/// it's exactly the per-attempt log a studio needs to debug a failing endpoint.
/// </summary>
public class WebhookDelivery : TenantEntity
{
    public Guid WebhookEndpointId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public int? ResponseStatusCode { get; set; }
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
