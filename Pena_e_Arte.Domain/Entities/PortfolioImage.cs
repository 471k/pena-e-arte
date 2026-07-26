namespace Pena_e_Arte.Domain.Entities;

public class PortfolioImage : TenantEntity
{
    public Guid ArtistId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional tattoo style tag. Values are app-controlled; see TattooStyle constants.
    /// Max 50 chars. Null means untagged / "All".
    /// </summary>
    public string? Style { get; set; }

    // Navigation
    public Artist Artist { get; set; } = null!;
}
