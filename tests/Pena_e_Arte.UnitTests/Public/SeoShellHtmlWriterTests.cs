using System.Text.Json;
using FluentAssertions;
using Pena_e_Arte.Application.Public;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.UnitTests.Public;

public class SeoShellHtmlWriterTests
{
    private const string Base = "https://tattooos.co";

    private static PublicStudioResponse MakeStudio(
        string name = "Ink Studio",
        string slug = "ink-studio",
        string? description = "The best ink in town.",
        string? coverImageUrl = "https://cdn.example.com/cover.jpg",
        double latitude = 41.1579,
        double longitude = -8.6291,
        double? averageRating = 4.5,
        int reviewCount = 3,
        IReadOnlyList<PublicArtistSummary>? artists = null,
        IReadOnlyList<PublicStudioHoursResponse>? hours = null) =>
        new(
            Guid.NewGuid(), name, slug, "Porto", latitude, longitude, description, coverImageUrl,
            "+351 912 345 678", averageRating, reviewCount, [],
            artists ?? [],
            true, [], hours ?? [], "Europe/Lisbon");

    private static PublicArtistResponse MakeArtist(
        string name = "Elena Martins",
        string slug = "elena-martins",
        string? bio = "Neo-traditional specialist.",
        string? profileImageUrl = "https://cdn.example.com/elena.jpg",
        string studioName = "Ink Studio",
        string studioSlug = "ink-studio") =>
        new(
            Guid.NewGuid(), name, slug, bio, profileImageUrl, [], [], null, 4.8, 5,
            studioName, studioSlug, true, false, []);

    // ── Studio shell ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildStudioShell_TitleDescriptionAndCanonicalMatchTheSpaPage()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(), Base);

        html.Should().Contain("<title>Ink Studio — Book a Tattoo on TattooOS</title>");
        html.Should().Contain("content=\"The best ink in town.\"");
        html.Should().Contain("rel=\"canonical\" href=\"https://tattooos.co/s/ink-studio\"");
        html.Should().Contain("property=\"og:url\" content=\"https://tattooos.co/s/ink-studio\"");
        html.Should().Contain("name=\"robots\" content=\"index,follow\"");
    }

    [Fact]
    public void BuildStudioShell_UsesTheFallbackDescription_WhenNoneIsSet()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(description: null), Base);

        html.Should().Contain("Book your next tattoo at Ink Studio.");
    }

    [Fact]
    public void BuildStudioShell_OmitsImageTags_WhenCoverImageIsNotHttps()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(coverImageUrl: "http://cdn.example.com/cover.jpg"), Base);

        html.Should().NotContain("og:image");
        html.Should().NotContain("twitter:image");
    }

    [Fact]
    public void BuildStudioShell_OmitsImageTags_WhenCoverImageIsNull()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(coverImageUrl: null), Base);

        html.Should().NotContain("og:image");
    }

    [Fact]
    public void BuildStudioShell_IncludesImageTags_WhenCoverImageIsHttps()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(), Base);

        html.Should().Contain("property=\"og:image\" content=\"https://cdn.example.com/cover.jpg\"");
        html.Should().Contain("name=\"twitter:image\" content=\"https://cdn.example.com/cover.jpg\"");
    }

    [Fact]
    public void BuildStudioShell_ListsEachArtistAsALinkToTheirCanonicalUrl()
    {
        PublicArtistSummary[] artists =
        [
            new(Guid.NewGuid(), "Elena Martins", "elena-martins", null, null, [], null, 0),
            new(Guid.NewGuid(), "Sam Okafor", "sam-okafor", null, null, [], null, 0),
        ];

        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(artists: artists), Base);

        html.Should().Contain("<a href=\"https://tattooos.co/artist/elena-martins\">Elena Martins</a>");
        html.Should().Contain("<a href=\"https://tattooos.co/artist/sam-okafor\">Sam Okafor</a>");
    }

    [Fact]
    public void BuildStudioShell_EndsWithALinkToItsOwnCanonicalUrl()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(), Base);
        int bodyStart = html.IndexOf("<body>", StringComparison.Ordinal);

        html.Should().Contain("<a href=\"https://tattooos.co/s/ink-studio\">View on TattooOS</a>");
        html.IndexOf("View on TattooOS", StringComparison.Ordinal).Should().BeGreaterThan(bodyStart);
    }

    [Fact]
    public void BuildStudioShell_HtmlEncodesNameAndDescription()
    {
        PublicStudioResponse studio = MakeStudio(
            name: "Ink & <Steel> Studio",
            description: "The \"best\" ink & steel <in> town.");

        string html = SeoShellHtmlWriter.BuildStudioShell(studio, Base);

        html.Should().NotContain("<Steel>");
        html.Should().NotContain("<in>");
        html.Should().Contain("Ink &amp; &lt;Steel&gt; Studio");
        html.Should().Contain("The &quot;best&quot; ink &amp; steel &lt;in&gt; town.");
    }

    [Fact]
    public void BuildStudioShell_JsonLdCannotBeBrokenOutOfByANameContainingScriptTags()
    {
        PublicStudioResponse studio = MakeStudio(name: "</script><script>alert(1)</script>");

        string html = SeoShellHtmlWriter.BuildStudioShell(studio, Base);
        int scriptStart = html.IndexOf("application/ld+json\">", StringComparison.Ordinal) + "application/ld+json\">".Length;
        int scriptEnd = html.IndexOf("</script>", scriptStart, StringComparison.Ordinal);
        string jsonLd = html[scriptStart..scriptEnd];

        html.Should().NotContain("<script>alert(1)</script>");
        JsonDocument doc = JsonDocument.Parse(jsonLd);
        doc.RootElement.GetProperty("name").GetString().Should().Be("</script><script>alert(1)</script>");
    }

    [Fact]
    public void BuildStudioShell_JsonLd_HasTheCoreTattooParlorFields()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        doc.RootElement.GetProperty("@type").GetString().Should().Be("TattooParlor");
        doc.RootElement.GetProperty("name").GetString().Should().Be("Ink Studio");
        doc.RootElement.GetProperty("url").GetString().Should().Be("https://tattooos.co/s/ink-studio");
        doc.RootElement.GetProperty("address").GetProperty("addressLocality").GetString().Should().Be("Porto");
    }

    [Fact]
    public void BuildStudioShell_JsonLd_OmitsGeo_WhenLocationIsUnpinned()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(latitude: 0, longitude: 0), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        doc.RootElement.TryGetProperty("geo", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildStudioShell_JsonLd_IncludesGeo_WhenLocationIsPinned()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        doc.RootElement.GetProperty("geo").GetProperty("latitude").GetDouble().Should().Be(41.1579);
    }

    [Fact]
    public void BuildStudioShell_JsonLd_OmitsAggregateRating_WhenThereAreNoReviews()
    {
        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(reviewCount: 0, averageRating: null), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        doc.RootElement.TryGetProperty("aggregateRating", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildStudioShell_JsonLd_IncludesOpeningHoursForOpenDaysOnly()
    {
        PublicStudioHoursResponse[] hours =
        [
            new(DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), true),
            new(DayOfWeek.Sunday, TimeSpan.Zero, TimeSpan.Zero, false),
        ];

        string html = SeoShellHtmlWriter.BuildStudioShell(MakeStudio(hours: hours), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        JsonElement spec = doc.RootElement.GetProperty("openingHoursSpecification");
        spec.GetArrayLength().Should().Be(1);
        spec[0].GetProperty("dayOfWeek").GetString().Should().Be("Monday");
        spec[0].GetProperty("opens").GetString().Should().Be("09:00");
        spec[0].GetProperty("closes").GetString().Should().Be("18:00");
    }

    // ── Artist shell ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildArtistShell_TitleDescriptionAndCanonicalMatchTheSpaPage()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(), Base);

        html.Should().Contain("<title>Elena Martins — Tattoo Artist on TattooOS</title>");
        html.Should().Contain("content=\"Neo-traditional specialist.\"");
        html.Should().Contain("rel=\"canonical\" href=\"https://tattooos.co/artist/elena-martins\"");
    }

    [Fact]
    public void BuildArtistShell_UsesTheFallbackDescription_WhenNoBioIsSet()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(bio: null), Base);

        html.Should().Contain("View the tattoo portfolio of Elena Martins.");
    }

    [Fact]
    public void BuildArtistShell_OmitsImageTags_WhenProfileImageIsNotHttps()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(profileImageUrl: "http://cdn.example.com/e.jpg"), Base);

        html.Should().NotContain("og:image");
    }

    [Fact]
    public void BuildArtistShell_JsonLd_IsAPersonWithTheJobTitle()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(), Base);
        JsonDocument doc = JsonDocument.Parse(ExtractJsonLd(html));

        doc.RootElement.GetProperty("@type").GetString().Should().Be("Person");
        doc.RootElement.GetProperty("jobTitle").GetString().Should().Be("Tattoo Artist");
        doc.RootElement.GetProperty("url").GetString().Should().Be("https://tattooos.co/artist/elena-martins");
    }

    [Fact]
    public void BuildArtistShell_HtmlEncodesTheBio()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(bio: "5 years of <b>experience</b> & counting"), Base);

        html.Should().NotContain("<b>experience</b>");
        html.Should().Contain("&lt;b&gt;experience&lt;/b&gt;");
    }

    [Fact]
    public void BuildArtistShell_ShowsTheStudioName()
    {
        string html = SeoShellHtmlWriter.BuildArtistShell(MakeArtist(studioName: "Dark Canvas"), Base);

        html.Should().Contain("<p>Dark Canvas</p>");
    }

    private static string ExtractJsonLd(string html)
    {
        int start = html.IndexOf("application/ld+json\">", StringComparison.Ordinal) + "application/ld+json\">".Length;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return html[start..end];
    }
}
