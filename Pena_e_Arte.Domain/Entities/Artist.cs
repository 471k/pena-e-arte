namespace Pena_e_Arte.Domain.Entities;

public class Artist : TenantEntity
{
    public Guid?   UserId          { get; set; }
    public string  FirstName       { get; set; } = string.Empty;
    public string  LastName        { get; set; } = string.Empty;
    public string  Email           { get; set; } = string.Empty;
    public string? Specializations { get; set; }

    public ICollection<Appointment>  Appointments  { get; set; } = [];
    public ICollection<TattooRecord> TattooRecords { get; set; } = [];
}
