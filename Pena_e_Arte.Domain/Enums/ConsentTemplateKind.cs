namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// Discriminates the kind of consent a <see cref="Entities.ConsentTemplate"/> captures,
/// so one table serves both the per-appointment tattoo consent and the cross-tenant
/// health-data (allergies + medical notes) sharing consent — no second consent
/// mechanism or table.
/// </summary>
public enum ConsentTemplateKind
{
    /// <summary>Consent signed before a tattoo session (risks/procedures).</summary>
    AppointmentConsent,

    /// <summary>
    /// Explicit consent to share the portable tattoo profile — display name, body-map
    /// locations, and tattoo history (photos, descriptions, artist) — with a second,
    /// unrelated studio via the portable-profile opt-in.
    ///
    /// NOTE: verified against PortableClientProfile / PortableProfileService at build time —
    /// the cross-tenant read model does NOT expose medical notes or allergies (those stay in
    /// the originating studio). The original epic assumed Art. 9 health data was shared here;
    /// it is not. Named accordingly so the consent copy stays truthful.
    /// </summary>
    CrossTenantProfileSharing,

    /// <summary>Consent to submit free-text medical/tattoo-history data via the intake form
    /// (Law 124/2024 (Albania) / GDPR Art. 9 special-category data).</summary>
    IntakeFormConsent,
}
