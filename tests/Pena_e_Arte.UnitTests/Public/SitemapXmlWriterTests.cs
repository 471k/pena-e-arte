using System.Xml.Linq;
using FluentAssertions;
using Pena_e_Arte.Application.Public;
using Pena_e_Arte.Application.Public.Queries;

namespace Pena_e_Arte.UnitTests.Public;

public class SitemapXmlWriterTests
{
    private const string Base = "https://tattooos.co";
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Fact]
    public void Build_ProducesWellFormedXmlWithOneUrlPerEntry()
    {
        string xml = SitemapXmlWriter.Build(
            [new SitemapUrlEntry("/pricing", null), new SitemapUrlEntry("/s/ink", new DateTime(2026, 9, 1))],
            Base);

        XDocument doc = XDocument.Parse(xml);

        doc.Root!.Name.Should().Be(Ns + "urlset");
        doc.Root.Elements(Ns + "url").Should().HaveCount(2);
        doc.Root.Elements(Ns + "url").Select(u => u.Element(Ns + "loc")!.Value)
            .Should().Equal("https://tattooos.co/pricing", "https://tattooos.co/s/ink");
    }

    [Fact]
    public void Build_OmitsLastmodWhenNull_AndFormatsItWhenPresent()
    {
        string xml = SitemapXmlWriter.Build(
            [new SitemapUrlEntry("/faq", null), new SitemapUrlEntry("/s/ink", new DateTime(2026, 9, 5, 13, 45, 0))],
            Base);

        List<XElement> urls = XDocument.Parse(xml).Root!.Elements(Ns + "url").ToList();

        urls[0].Element(Ns + "lastmod").Should().BeNull();
        urls[1].Element(Ns + "lastmod")!.Value.Should().Be("2026-09-05");
    }

    [Fact]
    public void Build_EscapesXmlSpecialCharactersInThePath()
    {
        string xml = SitemapXmlWriter.Build([new SitemapUrlEntry("/s/a&b<c>", null)], Base);

        xml.Should().Contain("/s/a&amp;b&lt;c&gt;");
        XDocument.Parse(xml).Root!.Element(Ns + "url")!.Element(Ns + "loc")!.Value
            .Should().Be("https://tattooos.co/s/a&b<c>");
    }

    [Fact]
    public void Build_WithNoEntries_ProducesAValidEmptyUrlset()
    {
        string xml = SitemapXmlWriter.Build([], Base);

        XDocument.Parse(xml).Root!.Elements().Should().BeEmpty();
    }
}
