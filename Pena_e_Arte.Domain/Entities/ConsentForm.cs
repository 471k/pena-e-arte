namespace Pena_e_Arte.Domain.Entities;

public class ConsentForm : TenantEntity
{
    public Guid      ClientId      { get; set; }
    public Guid      AppointmentId { get; set; }
    public string?   FileUrl       { get; set; }
    public DateTime? SignedAt      { get; set; }
    public string?   SignatureData { get; set; }

    public Client      Client      { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
