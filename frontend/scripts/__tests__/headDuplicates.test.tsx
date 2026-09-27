import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { HEAD_END_MARKER, HEAD_START_MARKER } from "@/shared/seo/renderRouteHtml";

// Regression guard for the duplicate <head> tags the static index.html used to leave behind
// (two og:title / og:description / og:image once useDocumentMeta injected its own). The head below is
// seeded with the REAL marked region of index.html.

function indexHtmlHeadRegion(): string {
  const html = readFileSync(resolve(import.meta.dirname, "../../index.html"), "utf8");
  const start = html.indexOf(HEAD_START_MARKER) + HEAD_START_MARKER.length;
  return html.slice(start, html.indexOf(HEAD_END_MARKER));
}

function Page() {
  useDocumentMeta({
    title: "Pricing — TattooOS",
    description: "Simple pricing.",
    canonical: "https://tattooos.co/pricing",
  });
  return null;
}

function count(selector: string): number {
  return document.head.querySelectorAll(selector).length;
}

describe("head metadata after useDocumentMeta runs against the real index.html head", () => {
  beforeEach(() => {
    document.head.innerHTML = `<title>seed</title>${indexHtmlHeadRegion()}`;
  });
  afterEach(() => {
    cleanup();
    document.head.innerHTML = "";
  });

  it("starts with the default metadata in place, but with no canonical or og:url", () => {
    expect(count('meta[property="og:title"]')).toBe(1);
    // The default shell is served for every dynamic route too; a canonical of "/" there would tell
    // crawlers each studio/artist page duplicates the home page.
    expect(count('link[rel="canonical"]')).toBe(0);
    expect(count('meta[property="og:url"]')).toBe(0);
  });

  it("leaves exactly one of each tag, carrying the page's own values", () => {
    render(<Page />);

    for (const selector of [
      'meta[property="og:title"]',
      'meta[property="og:description"]',
      'meta[property="og:image"]',
      'meta[property="og:url"]',
      'meta[property="og:type"]',
      'meta[name="description"]',
      'meta[name="twitter:card"]',
      'meta[name="twitter:image"]',
      'link[rel="canonical"]',
    ]) {
      expect(count(selector), selector).toBe(1);
    }
    expect(document.title).toBe("Pricing — TattooOS");
    expect(document.head.querySelector('meta[property="og:title"]')?.getAttribute("content")).toBe("Pricing — TattooOS");
    expect(document.head.querySelector('link[rel="canonical"]')?.getAttribute("href")).toBe("https://tattooos.co/pricing");
  });

  it("keeps the default og:image when the page supplies none, and does not leave a relative one", () => {
    render(<Page />);

    const image = document.head.querySelector('meta[property="og:image"]')?.getAttribute("content") ?? "";
    expect(image.startsWith("https://")).toBe(true);
  });
});
