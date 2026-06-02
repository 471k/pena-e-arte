namespace Pena_e_Arte.Domain.Entities;

public class TattooRecord : TenantEntity
{
    public Guid         ClientId      { get; set; }
    public Guid         ArtistId      { get; set; }
    public Guid?        AppointmentId { get; set; }
    public string       Description   { get; set; } = string.Empty;
    public string       BodyLocation  { get; set; } = string.Empty;
    public List<string> PhotoUrls     { get; set; } = [];
    public DateTime     CompletedAt   { get; set; }

    public Client Client { get; set; } = null!;
    public Artist Artist { get; set; } = null!;
}
