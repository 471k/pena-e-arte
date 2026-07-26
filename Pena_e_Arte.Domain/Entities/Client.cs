namespace Pena_e_Arte.Domain.Entities;

public class Client : TenantEntity
{
    public Guid? UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    public ClientProfile? Profile { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<TattooRecord> TattooRecords { get; set; } = [];
}
