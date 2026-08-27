namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Canonical portfolio-image category identifiers — what stage of work a portfolio photo shows.
/// Used on PortfolioImage.Category. Independent of TattooStyle (Style is the tattoo's artistic
/// style; Category is fresh/healed/design). Not related to the Design/DesignApproval/DesignRevision
/// entities (a per-client commissioned-tattoo workflow) — this is a public portfolio label only.
/// Keep in sync with CATEGORY_OPTIONS in ArtistDetailPage.tsx and CATEGORIES in PortfolioFeed.tsx.
/// </summary>
public static class PortfolioImageCategory
{
    public const string FreshTattoo = "fresh";
    public const string HealedTattoo = "healed";
    public const string Design = "design";

    public static readonly IReadOnlyList<string> All = [FreshTattoo, HealedTattoo, Design];
}
