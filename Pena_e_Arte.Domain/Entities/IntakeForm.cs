namespace Pena_e_Arte.Domain.Entities;

public class IntakeForm : TenantEntity
{
    public Guid      ClientId      { get; set; }
    public Guid?     AppointmentId { get; set; }
    public string    FormData      { get; set; } = string.Empty;
    public string?   FileUrl       { get; set; }
    public DateTime? SubmittedAt   { get; set; }

    public Client Client { get; set; } = null!;
}
