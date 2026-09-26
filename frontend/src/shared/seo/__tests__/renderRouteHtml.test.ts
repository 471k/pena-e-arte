import { describe, it, expect } from "vitest";
import {
  HEAD_END_MARKER,
  HEAD_START_MARKER,
  escapeHtml,
  renderHead,
  renderRouteHtml,
} from "@/shared/seo/renderRouteHtml";
import type { RouteMeta } from "@/shared/seo/siteRoutes";

const TEMPLATE = `<!doctype html>
<html><head>
    <meta charset="UTF-8" />
    ${HEAD_START_MARKER}
    <title>Default</title>
    <meta data-doc-meta="1" name="description" content="default" />
    ${HEAD_END_MARKER}
    <link rel="stylesheet" href="/assets/app.css" />
  </head><body><div id="root"></div><script src="/assets/app.js"></script></body></html>`;

const ROUTE: RouteMeta = { path: "/pricing", title: "Pricing — TattooOS", description: "Simple pricing." };

function count(haystack: string, needle: RegExp): number {
  return haystack.match(needle)?.length ?? 0;
}

describe("renderRouteHtml", () => {
  it("replaces only the marked head region and leaves the rest of the document untouched", () => {
    const html = renderRouteHtml(TEMPLATE, ROUTE);

    expect(html).toContain('<link rel="stylesheet" href="/assets/app.css" />');
    expect(html).toContain('<div id="root"></div><script src="/assets/app.js"></script>');
    expect(html).toContain('<meta charset="UTF-8" />');
    expect(html).not.toContain("<title>Default</title>");
  });

  it("emits exactly one title, one description and one canonical, pointing at the apex", () => {
    const html = renderRouteHtml(TEMPLATE, ROUTE);

    expect(count(html, /<title>/g)).toBe(1);
    expect(html).toContain("<title>Pricing — TattooOS</title>");
    expect(count(html, /name="description"/g)).toBe(1);
    expect(count(html, /rel="canonical"/g)).toBe(1);
    expect(html).toContain('rel="canonical" href="https://tattooos.co/pricing"');
    expect(html).toContain('property="og:url" content="https://tattooos.co/pricing"');
  });

  it("emits Open Graph and Twitter tags with an absolute image URL", () => {
    const html = renderRouteHtml(TEMPLATE, ROUTE);

    expect(html).toContain('property="og:image" content="https://tattooos.co/og-image.png"');
    expect(html).toContain('name="twitter:image" content="https://tattooos.co/og-image.png"');
    expect(html).toContain('name="twitter:card" content="summary_large_image"');
    expect(html).toContain('property="og:title" content="Pricing — TattooOS"');
    expect(html).toContain('name="twitter:description" content="Simple pricing."');
  });

  it("tags every meta/link with data-doc-meta so useDocumentMeta replaces them instead of duplicating", () => {
    const head = renderHead(ROUTE);
    const tags = head.split("\n").map((line) => line.trim()).filter((line) => line.startsWith("<meta") || line.startsWith("<link"));

    expect(tags.length).toBeGreaterThan(8);
    for (const tag of tags) expect(tag).toContain('data-doc-meta="1"');
  });

  it("escapes HTML-significant characters in values", () => {
    const html = renderRouteHtml(TEMPLATE, {
      path: "/x",
      title: 'A & B <script>"x"</script>',
      description: `It's "quoted" & <b>bold</b>`,
    });

    expect(html).toContain("<title>A &amp; B &lt;script&gt;&quot;x&quot;&lt;/script&gt;</title>");
    expect(html).not.toContain("<script>\"x\"");
    expect(html).toContain("&lt;b&gt;bold&lt;/b&gt;");
  });

  it("throws when a marker is missing, so a template edit cannot silently ship default metadata", () => {
    expect(() => renderRouteHtml(TEMPLATE.replace(HEAD_START_MARKER, ""), ROUTE)).toThrow(/markers/);
    expect(() => renderRouteHtml(TEMPLATE.replace(HEAD_END_MARKER, ""), ROUTE)).toThrow(/markers/);
  });

  it("throws when the markers are in the wrong order", () => {
    const swapped = TEMPLATE.replace(HEAD_START_MARKER, "@@S@@")
      .replace(HEAD_END_MARKER, HEAD_START_MARKER)
      .replace("@@S@@", HEAD_END_MARKER);

    expect(() => renderRouteHtml(swapped, ROUTE)).toThrow(/markers/);
  });

  it("is idempotent when applied to its own output for another route", () => {
    const once = renderRouteHtml(TEMPLATE, ROUTE);
    const twice = renderRouteHtml(once, { path: "/faq", title: "FAQ — TattooOS", description: "Answers." });

    expect(count(twice, /<title>/g)).toBe(1);
    expect(twice).toContain("<title>FAQ — TattooOS</title>");
    expect(twice).not.toContain("Pricing — TattooOS");
  });

  it("escapeHtml covers the four characters that matter inside an attribute", () => {
    expect(escapeHtml(`&<>"`)).toBe("&amp;&lt;&gt;&quot;");
  });
});
