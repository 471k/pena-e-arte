namespace Pena_e_Arte.Domain.Entities;

public class Artist : TenantEntity
{
    public Guid?        UserId          { get; set; }
    public string       FirstName       { get; set; } = string.Empty;
    public string       LastName        { get; set; } = string.Empty;
    public string       Email           { get; set; } = string.Empty;
    public string?      Specializations { get; set; }

    /// <summary>Hourly rate in EUR — the base for percent deposit rules. Null = not set.</summary>
    public decimal?     HourlyRate      { get; set; }
    public string?      Slug            { get; private set; }
    public string?      Bio             { get; set; }
    public List<string> PortfolioImages { get; set; } = [];

    public void SetSlug(string slug) => Slug = slug;

    public ICollection<Appointment>  Appointments  { get; set; } = [];
    public ICollection<TattooRecord> TattooRecords { get; set; } = [];
}
