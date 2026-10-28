using System.Net;
using System.Text.RegularExpressions;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Builds the plain-text alternative sent with every HTML email. HTML-only mail scores worse with
/// spam filters (C-11 production test, 27 Oct 2026: a guest booking email landed in spam with SPF,
/// DKIM and DMARC all passing), and some readers show only text. Link targets are kept
/// ("Set your password (https://…)") so a text-only reader can still act on the email.
/// </summary>
internal static class EmailPlainText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    private static readonly Regex HiddenBlocks = new(@"<(head|style|script|title)\b[^>]*>.*?</\1\s*>", Options, Timeout);
    private static readonly Regex Links = new(@"<a\b[^>]*?\bhref\s*=\s*""([^""]*)""[^>]*>(.*?)</a\s*>", Options, Timeout);
    private static readonly Regex LineBreaks = new(@"<br\s*/?>", Options, Timeout);
    private static readonly Regex Rules = new(@"<hr\b[^>]*>", Options, Timeout);
    private static readonly Regex BlockEnds = new(@"</(p|div|h[1-6]|li|tr|table|ul|ol)\s*>", Options, Timeout);
    private static readonly Regex Tags = new(@"<[^>]*>", Options, Timeout);
    private static readonly Regex Spaces = new(@"[ \t\f\v]+", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.CultureInvariant, Timeout);

    public static string FromHtml(string html)
    {
        string text = HiddenBlocks.Replace(html, "");
        text = Links.Replace(text, link =>
        {
            string href = WebUtility.HtmlDecode(link.Groups[1].Value).Trim();
            string label = WebUtility.HtmlDecode(Tags.Replace(link.Groups[2].Value, "")).Trim();
            return label.Length == 0 || label == href ? href : $"{label} ({href})";
        });
        text = LineBreaks.Replace(text, "\n");
        text = Rules.Replace(text, "\n----\n");
        text = BlockEnds.Replace(text, "\n\n");
        text = Tags.Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace("\r\n", "\n");
        text = string.Join('\n', text.Split('\n').Select(line => Spaces.Replace(line, " ").Trim()));
        return BlankLines.Replace(text, "\n\n").Trim();
    }
}
