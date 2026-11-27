import { describe, it, expect } from "vitest";
// `?raw` (declared by vite/client) returns the file's raw text content — avoids needing Node's
// `fs`/`path` (not in this project's browser-scoped tsconfig.app.json `types`) just to read a
// static asset for a plain unit test. Not a browser-level PWA/Lighthouse audit; this repo has no
// Lighthouse CI step to hook into.
import manifestRaw from "../../../public/manifest.json?raw";
import indexHtml from "../../../index.html?raw";

describe("manifest.json", () => {
  it("is valid JSON", () => {
    expect(() => JSON.parse(manifestRaw)).not.toThrow();
  });

  it("has the required PWA fields", () => {
    const manifest = JSON.parse(manifestRaw);

    expect(manifest.name).toBeTruthy();
    expect(manifest.start_url).toBeTruthy();
    expect(manifest.display).toBe("standalone");
    expect(Array.isArray(manifest.icons)).toBe(true);
    expect(manifest.icons.length).toBeGreaterThan(0);
  });

  it("has at least one valid icon entry with src, sizes, and type", () => {
    const manifest = JSON.parse(manifestRaw);

    expect(manifest.icons).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          src: expect.stringMatching(/\.png$/),
          sizes: expect.stringMatching(/^\d+x\d+$/),
          type: "image/png",
        }),
      ]),
    );
  });

  // Brand master 1.0.0 (After Hours), 27 Nov 2026.
  interface ManifestIcon { src: string; sizes: string; type: string; purpose?: string }

  it("lists the package's four icons: 192 and 512, each as 'any' and as a separate maskable entry", () => {
    const icons = (JSON.parse(manifestRaw) as { icons: ManifestIcon[] }).icons;
    expect(icons).toHaveLength(4);
    expect(icons.filter((i) => i.purpose === "maskable")).toHaveLength(2);
    expect(icons.filter((i) => i.purpose === "any")).toHaveLength(2);
    for (const purpose of ["any", "maskable"]) {
      expect(icons.filter((i) => i.purpose === purpose).map((i) => i.sizes).sort()).toEqual(["192x192", "512x512"]);
    }
    expect(icons.map((i) => i.src).sort()).toEqual([
      "/icons/icon-192.png",
      "/icons/icon-512.png",
      "/icons/maskable-192.png",
      "/icons/maskable-512.png",
    ]);
  });

  it("uses the brand chrome colour and keeps the installed app's identity unchanged", () => {
    const manifest = JSON.parse(manifestRaw) as Record<string, unknown>;
    expect(manifest.theme_color).toBe("#19161D");
    expect(manifest.background_color).toBe("#19161D");
    // Identity fields an installed app is matched by: changing them would orphan installs.
    expect(manifest.name).toBe("TattooOS");
    expect(manifest.short_name).toBe("TattooOS");
    expect(manifest.start_url).toBe("/");
    expect(manifest.display).toBe("standalone");
  });
});

describe("index.html icon and chrome declarations (brand master 1.0.0)", () => {
  it("declares the ICO, the versioned SVG, the apple-touch icon and the #19161D theme colour", () => {
    expect(indexHtml).toContain('<link rel="icon" href="/favicon.ico?v=1.0.0" type="image/x-icon" sizes="16x16 32x32 48x48" />');
    expect(indexHtml).toContain('<link rel="icon" href="/favicon.svg?v=1.0.0" type="image/svg+xml" sizes="any" />');
    expect(indexHtml).toContain('<link rel="apple-touch-icon" href="/apple-touch-icon.png?v=1.0.0" sizes="180x180" />');
    expect(indexHtml).toContain('<meta name="theme-color" content="#19161D" />');
    expect(indexHtml).not.toContain("#7c3aed");
    expect(indexHtml).toContain('<link rel="manifest" href="/manifest.json" />');
  });

  it("keeps the per-route SEO region the prerender replaces", () => {
    expect(indexHtml).toContain("<!-- seo:head:start -->");
    expect(indexHtml).toContain("<!-- seo:head:end -->");
    expect(indexHtml.indexOf("seo:head:start")).toBeLessThan(indexHtml.indexOf("seo:head:end"));
  });
});
