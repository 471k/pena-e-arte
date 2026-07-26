namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A single synced Instagram media item belonging to an artist.
/// IsVisible controls whether it appears on the public portfolio without
/// deleting the synced record. No global query filter — see InstagramConnection.
/// </summary>
public class InstagramPost : TenantEntity
{
    public Guid ArtistId { get; set; }
    public string InstagramMediaId { get; set; } = "";
    public string MediaUrl { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public string? Caption { get; set; }

    /// <summary>IMAGE or CAROUSEL_ALBUM — VIDEO items are skipped during sync.</summary>
    public string MediaType { get; set; } = "";

    public DateTime PostedAt { get; set; }
    public bool IsVisible { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
