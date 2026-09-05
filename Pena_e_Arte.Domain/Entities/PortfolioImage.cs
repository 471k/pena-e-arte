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

    /// <summary>
    /// Optional portfolio category tag. Values are app-controlled; see PortfolioImageCategory
    /// constants. Max 20 chars. Null means uncategorized. Independent of Style.
    /// </summary>
    public string? Category { get; set; }

    // Navigation
    public Artist Artist { get; set; } = null!;
}
