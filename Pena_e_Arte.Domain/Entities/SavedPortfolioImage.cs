namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A portfolio image that a logged-in user has bookmarked.
/// Not tenant-scoped — the saving user may belong to a different studio.
/// </summary>
public class SavedPortfolioImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid PortfolioImageId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public PortfolioImage PortfolioImage { get; set; } = null!;
}
