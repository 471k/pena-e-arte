using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Domain.Entities;

public class ClientProfile : TenantEntity
{
    public Guid ClientId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? MedicalNotes { get; set; }
    public string? Allergies { get; set; }
    public BodyMap BodyMap { get; set; } = new();
    public bool AllowCrossTenantRead { get; private set; } = false;
    public DateTime? CrossTenantOptInAt { get; private set; }

    /// <summary>The cross-tenant profile-sharing consent template in force at opt-in.</summary>
    public Guid? CrossTenantConsentTemplateId { get; private set; }

    /// <summary>
    /// Immutable snapshot of the exact cross-tenant profile-sharing consent text the client
    /// agreed to when opting in — the same versioned-consent-with-snapshot pattern used for
    /// appointment consent, reused here rather than adding a second mechanism. (The shared
    /// read model is tattoo history + body map, not medical notes/allergies.)
    /// </summary>
    public string? CrossTenantConsentSnapshot { get; private set; }

    public Client Client { get; set; } = null!;

    public void OptInToCrossTenant(Guid? consentTemplateId = null, string? consentSnapshot = null)
    {
        AllowCrossTenantRead = true;
        CrossTenantOptInAt = DateTime.UtcNow;
        CrossTenantConsentTemplateId = consentTemplateId;
        CrossTenantConsentSnapshot = consentSnapshot;
        UpdatedAt = DateTime.UtcNow;
    }

    public void OptOutOfCrossTenant()
    {
        AllowCrossTenantRead = false;
        CrossTenantOptInAt = null;
        // Keep the historical snapshot/template id? No — opting out withdraws the consent;
        // there is no active consent to reference. The audit log (Phase 3f) is the durable
        // record that a withdrawal happened and when.
        CrossTenantConsentTemplateId = null;
        CrossTenantConsentSnapshot = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
