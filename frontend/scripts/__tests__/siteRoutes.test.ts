import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { DEFAULT_OG_IMAGE, ROUTE_META, SITE_URL, STATIC_ROUTES } from "@/shared/seo/siteRoutes";
import { SITE_URL as LEGAL_SITE_URL, SITE_TAGLINE, SITE_META_DESCRIPTION } from "@/shared/constants/legalEntity";

// Paths are relative to this test file (frontend/scripts/__tests__). This folder is outside tsc's
// `src` scope on purpose: these tests read repo files with Node APIs the app tsconfig does not expose.
function readRepoFile(relativeToThisFile: string): string {
  return readFileSync(resolve(import.meta.dirname, relativeToThisFile), "utf8");
}

describe("siteRoutes manifest", () => {
  it("has unique paths that all start with a slash", () => {
    const paths = STATIC_ROUTES.map((r) => r.path);

    expect(new Set(paths).size).toBe(paths.length);
    for (const path of paths) expect(path.startsWith("/")).toBe(true);
    expect(paths).not.toContain("/");
  });

  it("keeps titles and descriptions within sensible search-snippet lengths", () => {
    for (const route of STATIC_ROUTES) {
      expect(route.title.length, `${route.path} title`).toBeLessThanOrEqual(65);
      expect(route.title.length, `${route.path} title`).toBeGreaterThan(5);
      // Policy pages carry short factual descriptions; the floor only rejects an empty/placeholder one.
      expect(route.description.length, `${route.path} description`).toBeGreaterThanOrEqual(30);
      expect(route.description.length, `${route.path} description`).toBeLessThanOrEqual(170);
    }
  });

  it("gives every route a distinct title", () => {
    const titles = STATIC_ROUTES.map((r) => r.title);

    expect(new Set(titles).size).toBe(titles.length);
  });

  it("uses the same site URL as legalEntity.ts and an absolute og-image", () => {
    expect(SITE_URL).toBe(LEGAL_SITE_URL);
    expect(DEFAULT_OG_IMAGE).toBe(`${SITE_URL}/og-image.png`);
  });

  it("keeps the home-page defaults in index.html in step with legalEntity.ts", () => {
    const html = readRepoFile("../../index.html");

    expect(html).toContain(SITE_META_DESCRIPTION);
    // index.html escapes & as &amp; (correct HTML); legalEntity.ts holds the plain text.
    expect(html).toContain(SITE_TAGLINE.replace(/&/g, "&amp;"));
  });

  it("matches the backend sitemap's marketing list (drift guard)", () => {
    const source = readRepoFile("../../../Pena_e_Arte.Application/Public/Queries/GetSitemapUrlsQuery.cs");
    const block = source.slice(source.indexOf("Marketing ="), source.indexOf("];", source.indexOf("Marketing =")));
    const sitemapPaths = [...block.matchAll(/"(\/[^"]*)"/g)].map((m) => m[1]).sort();

    // The sitemap lists "/" plus every static route except the legal pages, which stay indexable
    // but are deliberately not listed.
    const legal = new Set(["/privacy", "/terms", "/refund-policy"]);
    const expected = ["/", ...STATIC_ROUTES.map((r) => r.path).filter((p) => !legal.has(p))].sort();

    expect(sitemapPaths).toEqual(expected);
  });

  it("indexes ROUTE_META by the same paths STATIC_ROUTES lists", () => {
    expect(Object.keys(ROUTE_META).sort()).toEqual(STATIC_ROUTES.map((r) => r.path).sort());
  });
});
