using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Appointment : TenantEntity
{
    public Guid             ArtistId        { get; set; }
    public Guid             ClientId        { get; set; }
    public DateTime         Date            { get; set; }
    public DateTime         EndDate         { get; set; }
    public int              DurationMinutes { get; set; }
    public AppointmentStatus Status         { get; set; }
    public DepositStatus    DepositStatus   { get; set; }
    public decimal          DepositAmount   { get; set; }
    public string?          Notes           { get; set; }

    public Artist  Artist  { get; set; } = null!;
    public Client  Client  { get; set; } = null!;
}
