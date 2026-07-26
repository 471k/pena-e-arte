namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Canonical tattoo style identifiers. Used on PortfolioImage.Style and as filter
/// chip values on the DiscoverPage. Keep in sync with STYLES constant in PortfolioFeed.tsx.
/// </summary>
public static class TattooStyle
{
    public const string Traditional = "traditional";
    public const string Realism = "realism";
    public const string Blackwork = "blackwork";
    public const string Geometric = "geometric";
    public const string Watercolor = "watercolor";
    public const string Fineline = "fineline";
    public const string NeoTraditional = "neo-traditional";
    public const string Japanese = "japanese";

    public static readonly IReadOnlyList<string> All =
        [Traditional, Realism, Blackwork, Geometric, Watercolor, Fineline, NeoTraditional, Japanese];
}
