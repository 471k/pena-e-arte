using System.Text.RegularExpressions;

namespace Pena_e_Arte.Domain.Utilities;

public static class SlugHelper
{
    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9\s-]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceOrDash = new(@"[\s-]+", RegexOptions.Compiled);

    public static string GenerateSlug(string name)
    {
        string slug = name.ToLowerInvariant();
        slug = NonAlphanumeric.Replace(slug, "");
        slug = WhitespaceOrDash.Replace(slug, "-");
        slug = slug.Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length > 0 ? slug : "studio";
    }
}
