namespace Pena_e_Arte.Domain.Entities;

public class ConsentForm : TenantEntity
{
    public Guid ClientId { get; set; }
    public Guid AppointmentId { get; set; }
    public string? FileUrl { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignatureData { get; set; }

    /// <summary>
    /// The <see cref="ConsentTemplate"/> that was active when this form was signed.
    /// Nullable because forms signed before consent versioning existed have no template.
    /// </summary>
    public Guid? ConsentTemplateId { get; set; }

    /// <summary>
    /// The EXACT consent text the client agreed to at signing time, captured verbatim.
    /// Never re-derived from <see cref="ConsentTemplateId"/> for display — the template
    /// may have been edited or superseded since. This immutable snapshot is the record
    /// of what was actually agreed. Nullable for pre-versioning forms.
    /// </summary>
    public string? ConsentTextSnapshot { get; set; }

    public Client Client { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
