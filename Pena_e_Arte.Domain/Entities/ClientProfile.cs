using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Domain.Entities;

public class ClientProfile : TenantEntity
{
    public Guid      ClientId     { get; set; }
    public DateOnly? DateOfBirth  { get; set; }
    public string?   MedicalNotes { get; set; }
    public string?   Allergies    { get; set; }
    public BodyMap   BodyMap      { get; set; } = new();

    public Client Client { get; set; } = null!;
}
