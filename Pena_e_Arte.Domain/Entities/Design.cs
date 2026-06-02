namespace Pena_e_Arte.Domain.Entities;

public class Design : TenantEntity
{
    public Guid    ClientId    { get; set; }
    public Guid    ArtistId    { get; set; }
    public string  Title       { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Client  Client    { get; set; } = null!;
    public Artist  Artist    { get; set; } = null!;
    public ICollection<DesignRevision> Revisions { get; set; } = [];
}
