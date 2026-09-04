namespace Pena_e_Arte.Domain.Entities;

public class IntakeForm : TenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string FormData { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// The <see cref="ConsentTemplate"/> (kind <see cref="Enums.ConsentTemplateKind.IntakeFormConsent"/>)
    /// active when this form was submitted. Nullable because forms submitted before this consent
    /// existed have none of these three fields set — not backfilled.
    /// </summary>
    public Guid? ConsentTemplateId { get; set; }

    /// <summary>
    /// The EXACT consent text the client agreed to at submission time, captured verbatim —
    /// never re-derived from <see cref="ConsentTemplateId"/> for display, since the template may
    /// have been edited or superseded since. Nullable for pre-consent forms.
    /// </summary>
    public string? ConsentTextSnapshot { get; set; }
    public DateTime? ConsentedAt { get; set; }

    public Client Client { get; set; } = null!;
}
