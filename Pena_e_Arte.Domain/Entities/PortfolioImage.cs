namespace Pena_e_Arte.Domain.Entities;

public class PortfolioImage : TenantEntity
{
    public Guid   ArtistId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;

    // Navigation
    public Artist Artist { get; set; } = null!;
}
