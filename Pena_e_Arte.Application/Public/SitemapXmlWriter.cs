using System.Security;
using System.Text;
using Pena_e_Arte.Application.Public.Queries;

namespace Pena_e_Arte.Application.Public;

/// <summary>Builds the sitemap.xml document. Pure, so it is unit-testable without HTTP.</summary>
public static class SitemapXmlWriter
{
    public static string Build(IEnumerable<SitemapUrlEntry> urls, string siteBaseUrl)
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (SitemapUrlEntry url in urls)
        {
            sb.Append("<url>");
            sb.Append("<loc>").Append(SecurityElement.Escape(siteBaseUrl + url.Path)).Append("</loc>");
            if (url.LastModified is DateTime lastModified)
                sb.Append("<lastmod>").Append(lastModified.ToString("yyyy-MM-dd")).Append("</lastmod>");
            sb.Append("</url>");
        }
        sb.Append("</urlset>");
        return sb.ToString();
    }
}
