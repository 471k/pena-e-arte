using System.Net;
using System.Text;
using System.Text.Json;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public;

/// <summary>
/// Renders the minimal crawler-facing HTML shell for a studio or artist public page
/// (search-visibility Phase 5) — for bots that read only the raw HTML (link-preview bots,
/// some crawlers). Title, description, canonical and the JSON-LD block mirror what the SPA's
/// useDocumentMeta/useStructuredData build for the same page (StudioPortfolioPage.tsx,
/// ArtistPortfolioPage.tsx), so the shell's content matches what a human visitor sees.
///
/// Every dynamic value is HTML-encoded (WebUtility.HtmlEncode). JSON-LD is serialised with
/// System.Text.Json's default encoder, which escapes '&lt;', so a name can never close the
/// &lt;script&gt; tag early. Pure and unit-tested without HTTP.
/// </summary>
public static class SeoShellHtmlWriter
{
    public static string BuildStudioShell(PublicStudioResponse studio, string siteBaseUrl)
    {
        string canonical = $"{siteBaseUrl}/s/{studio.Slug}";
        string title = $"{studio.Name} — Book a Tattoo on TattooOS";
        string description = studio.Description ?? $"Book your next tattoo at {studio.Name}.";
        string? image = HttpsImageOrNull(studio.CoverImageUrl);

        Dictionary<string, object?> jsonLd = new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "TattooParlor",
            ["name"] = studio.Name,
            ["description"] = description,
            ["url"] = canonical,
            ["image"] = image,
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["addressLocality"] = studio.City,
            },
        };

        if (HasPinnedLocation(studio.Latitude, studio.Longitude))
        {
            jsonLd["geo"] = new Dictionary<string, object?>
            {
                ["@type"] = "GeoCoordinates",
                ["latitude"] = studio.Latitude,
                ["longitude"] = studio.Longitude,
            };
        }

        if (studio.ReviewCount > 0)
        {
            jsonLd["aggregateRating"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = studio.AverageRating,
                ["reviewCount"] = studio.ReviewCount,
            };
        }

        List<PublicStudioHoursResponse> openHours = studio.Hours.Where(h => h.IsOpen).ToList();
        if (openHours.Count > 0)
        {
            jsonLd["openingHoursSpecification"] = openHours.Select(h => new Dictionary<string, object?>
            {
                ["@type"] = "OpeningHoursSpecification",
                ["dayOfWeek"] = SchemaDayName(h.DayOfWeek),
                ["opens"] = h.StartTime.ToString(@"hh\:mm"),
                ["closes"] = h.EndTime.ToString(@"hh\:mm"),
            }).ToList();
        }

        StringBuilder body = new();
        body.Append("<h1>").Append(Encode(studio.Name)).Append("</h1>");
        body.Append("<p>").Append(Encode(description)).Append("</p>");
        body.Append("<p>").Append(Encode(studio.City)).Append("</p>");
        if (studio.Artists.Count > 0)
        {
            body.Append("<ul>");
            foreach (PublicArtistSummary artist in studio.Artists)
            {
                string artistUrl = $"{siteBaseUrl}/artist/{artist.Slug}";
                body.Append("<li><a href=\"").Append(Encode(artistUrl)).Append("\">")
                    .Append(Encode(artist.Name)).Append("</a></li>");
            }
            body.Append("</ul>");
        }

        return BuildDocument(title, description, canonical, image, jsonLd, body.ToString());
    }

    public static string BuildArtistShell(PublicArtistResponse artist, string siteBaseUrl)
    {
        string canonical = $"{siteBaseUrl}/artist/{artist.Slug}";
        string title = $"{artist.Name} — Tattoo Artist on TattooOS";
        string description = artist.Bio ?? $"View the tattoo portfolio of {artist.Name}.";
        string? image = HttpsImageOrNull(artist.ProfileImageUrl);

        Dictionary<string, object?> jsonLd = new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["jobTitle"] = "Tattoo Artist",
            ["name"] = artist.Name,
            ["description"] = description,
            ["image"] = image,
            ["url"] = canonical,
        };

        StringBuilder body = new();
        body.Append("<h1>").Append(Encode(artist.Name)).Append("</h1>");
        body.Append("<p>").Append(Encode(description)).Append("</p>");
        body.Append("<p>").Append(Encode(artist.StudioName)).Append("</p>");

        return BuildDocument(title, description, canonical, image, jsonLd, body.ToString());
    }

    private static string BuildDocument(
        string title,
        string description,
        string canonical,
        string? image,
        Dictionary<string, object?> jsonLd,
        string bodyHtml)
    {
        string encodedTitle = Encode(title);
        string encodedDescription = Encode(description);
        string encodedCanonical = Encode(canonical);
        // The default encoder (JavaScriptEncoder.Default, not UnsafeRelaxedJsonEscaping) escapes
        // '<', '>', '&', quotes and non-ASCII — a JSON-LD value can never close the <script> tag.
        string jsonLdScript = JsonSerializer.Serialize(jsonLd);

        StringBuilder html = new();
        html.Append("<!doctype html><html lang=\"en\"><head>");
        html.Append("<meta charset=\"UTF-8\" />");
        html.Append("<title>").Append(encodedTitle).Append("</title>");
        html.Append("<meta name=\"description\" content=\"").Append(encodedDescription).Append("\" />");
        html.Append("<meta name=\"robots\" content=\"index,follow\" />");
        html.Append("<link rel=\"canonical\" href=\"").Append(encodedCanonical).Append("\" />");
        html.Append("<meta property=\"og:type\" content=\"website\" />");
        html.Append("<meta property=\"og:url\" content=\"").Append(encodedCanonical).Append("\" />");
        html.Append("<meta property=\"og:title\" content=\"").Append(encodedTitle).Append("\" />");
        html.Append("<meta property=\"og:description\" content=\"").Append(encodedDescription).Append("\" />");
        if (image is not null)
            html.Append("<meta property=\"og:image\" content=\"").Append(Encode(image)).Append("\" />");
        html.Append("<meta name=\"twitter:card\" content=\"summary_large_image\" />");
        html.Append("<meta name=\"twitter:title\" content=\"").Append(encodedTitle).Append("\" />");
        html.Append("<meta name=\"twitter:description\" content=\"").Append(encodedDescription).Append("\" />");
        if (image is not null)
            html.Append("<meta name=\"twitter:image\" content=\"").Append(Encode(image)).Append("\" />");
        html.Append("<script type=\"application/ld+json\">").Append(jsonLdScript).Append("</script>");
        html.Append("</head><body>");
        html.Append(bodyHtml);
        html.Append("<p><a href=\"").Append(encodedCanonical).Append("\">View on TattooOS</a></p>");
        html.Append("</body></html>");
        return html.ToString();
    }

    // Mirrors StudioMeta's ogImage={coverImageUrl ?? undefined} restricted to https, per the prompt.
    private static string? HttpsImageOrNull(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
            ? url
            : null;

    // Mirrors frontend/src/shared/utils/googleMaps.ts hasPinnedLocation.
    private static bool HasPinnedLocation(double latitude, double longitude) =>
        !double.IsNaN(latitude) && !double.IsNaN(longitude) && !(latitude == 0 && longitude == 0);

    // Mirrors StudioPortfolioPage.tsx's SCHEMA_DAY_NAMES (JS/C# share the Sunday=0 DayOfWeek order).
    private static string SchemaDayName(DayOfWeek day) => day.ToString();

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
