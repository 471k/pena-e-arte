using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A versioned, immutable-once-effective body of consent text. Deliberately NOT a
/// TenantEntity: <see cref="StudioId"/> is nullable (null = platform-default template
/// used when a studio has no custom one), and there is no EF Core global query filter —
/// exactly the same nullable-studio, authorize-in-the-handler shape as
/// <see cref="AuditLogEntry"/>. Resolution of the active template for a studio is done
/// explicitly in the query/command handlers (see ConsentTemplateResolver), never via a
/// tenant filter.
/// </summary>
public class ConsentTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null = platform-default template (applies to every studio without its own).</summary>
    public Guid? StudioId { get; set; }

    public ConsentTemplateKind Kind { get; set; } = ConsentTemplateKind.AppointmentConsent;

    /// <summary>Human-readable version label, e.g. "1.0" or "2026-07".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The exact legal/consent body shown to and agreed by the client.</summary>
    public string BodyText { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
