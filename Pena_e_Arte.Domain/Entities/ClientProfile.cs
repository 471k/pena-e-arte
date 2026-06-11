using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Domain.Entities;

public class ClientProfile : TenantEntity
{
    public Guid      ClientId               { get; set; }
    public DateOnly? DateOfBirth            { get; set; }
    public string?   MedicalNotes          { get; set; }
    public string?   Allergies             { get; set; }
    public BodyMap   BodyMap               { get; set; } = new();
    public bool      AllowCrossTenantRead  { get; private set; } = false;
    public DateTime? CrossTenantOptInAt    { get; private set; }

    public Client Client { get; set; } = null!;

    public void OptInToCrossTenant()
    {
        AllowCrossTenantRead = true;
        CrossTenantOptInAt   = DateTime.UtcNow;
        UpdatedAt            = DateTime.UtcNow;
    }

    public void OptOutOfCrossTenant()
    {
        AllowCrossTenantRead = false;
        CrossTenantOptInAt   = null;
        UpdatedAt            = DateTime.UtcNow;
    }
}
