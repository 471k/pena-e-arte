namespace Pena_e_Arte.Domain.Entities;

public class Design : TenantEntity
{
    /// <summary>Null for a reusable flash/catalog item template — see IsCatalogItem. Every
    /// other design (the normal, organically-created case) always has a real client.</summary>
    public Guid? ClientId { get; set; }
    public Guid ArtistId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>True for a reusable flash-catalog template the artist offers to any client —
    /// mutually exclusive with having a ClientId. See MarkDesignAsCatalogItemCommand.</summary>
    public bool IsCatalogItem { get; set; }

    /// <summary>Only meaningful when IsCatalogItem is true.</summary>
    public decimal? Price { get; set; }

    public Client? Client { get; set; }
    public Artist Artist { get; set; } = null!;
    public ICollection<DesignRevision> Revisions { get; set; } = [];
}
