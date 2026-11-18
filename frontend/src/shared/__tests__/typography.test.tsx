import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
// `?raw` (declared by vite/client) returns the file's source text, untransformed.
import indexCss from "../../index.css?raw";
import indexHtml from "../../../index.html?raw";
import { Wordmark } from "@/shared/components/Wordmark";

// After Hours slice 5 (2026-11-18): the type, radius and breakpoint decisions, pinned.
// jsdom cannot load a webfont or compute a used font, so this pins the SOURCE contract; the
// rendered pass in the PR is the proof that the faces actually paint.

const stripComments = (s: string) => s.replace(/\/\*[\s\S]*?\*\//g, "").replace(/<!--[\s\S]*?-->/g, "");
const css = stripComments(indexCss);

const appSources = import.meta.glob<string>(
  ["/src/**/*.{ts,tsx,css}", "!/src/**/__tests__/**", "!/src/**/*.test.{ts,tsx}", "!/src/test/**"],
  { query: "?raw", import: "default", eager: true },
);
const publicFontFiles = Object.keys(import.meta.glob("/public/fonts/*", { query: "?url", eager: true }));
const licences = import.meta.glob<string>("/public/fonts/*.txt", { query: "?raw", import: "default", eager: true });
const servedTtf = Object.keys(import.meta.glob("/public/**/*.{ttf,otf}", { query: "?url", eager: true }));

/** Every top-level `@theme { … }` declaration, comments stripped. */
function themeDecls(): Map<string, string> {
  const body = /@theme\s*\{([\s\S]*?)\n\}/.exec(css)?.[1] ?? "";
  const decls = new Map<string, string>();
  for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) decls.set(m[1], m[2].trim());
  return decls;
}

function fontFaces(): { family: string; weight: string; style: string; src: string }[] {
  return [...css.matchAll(/@font-face\s*\{([^}]*)\}/g)].map(([, b]) => ({
    family: /font-family:\s*"([^"]+)"/.exec(b)?.[1] ?? "",
    weight: /font-weight:\s*(\d+)/.exec(b)?.[1] ?? "",
    style: /font-style:\s*(\w+)/.exec(b)?.[1] ?? "",
    src: /src:\s*([^;]+);/.exec(b)?.[1] ?? "",
  }));
}

describe("typography (slice 5)", () => {
  it("reads the source tree and the stylesheet (guards against a vacuous pass)", () => {
    expect(Object.keys(appSources).length).toBeGreaterThan(300);
    expect(themeDecls().size).toBeGreaterThan(20);
  });

  it("makes DM Sans the body family, and the body rule reads the token", () => {
    expect(themeDecls().get("--font-sans")).toMatch(/^"DM Sans",/);
    expect(css).toMatch(/body\s*\{[^}]*font-family:\s*var\(--font-sans\);/);
  });

  it("sets Fraunces nowhere: stylesheet, index.html, or any app source", () => {
    expect(indexCss).not.toMatch(/fraunces/i);
    expect(indexHtml).not.toMatch(/fraunces/i);
    const hits = Object.entries(appSources).filter(([, src]) => /fraunces/i.test(src)).map(([p]) => p);
    expect(hits).toEqual([]);
  });

  it("loads no third-party font origin", () => {
    expect(indexHtml).not.toMatch(/fonts\.(googleapis|gstatic)\.com/);
    expect(indexCss).not.toMatch(/@import\s+url\(\s*["']?https?:/);
  });

  it("ships exactly the faces the type scale names: DM Sans 400/500/600 and Bodoni Moda 700, woff2 only", () => {
    const faces = fontFaces();
    expect(faces.map((f) => `${f.family} ${f.weight} ${f.style}`).sort()).toEqual([
      "Bodoni Moda 700 normal",
      "DM Sans 400 normal",
      "DM Sans 500 normal",
      "DM Sans 600 normal",
    ]);
    for (const f of faces) {
      const url = /url\("([^"]+)"\)/.exec(f.src)?.[1] ?? "";
      expect(f.src, f.family).toMatch(/format\("woff2"\)$/);
      expect(publicFontFiles, url).toContain(`/public${url}`);
    }
  });

  it("serves no .ttf/.otf, and both OFL licences travel with the fonts", () => {
    expect(servedTtf).toEqual([]);
    expect(Object.keys(licences).sort()).toEqual(["/public/fonts/Bodoni-Moda-OFL.txt", "/public/fonts/DM-Sans-OFL.txt"]);
    for (const text of Object.values(licences)) expect(text).toContain("SIL Open Font License, Version 1.1");
    // Referenced from the code, not only present in the directory.
    expect(indexCss).toContain("public/fonts/DM-Sans-OFL.txt");
    expect(indexCss).toContain("public/fonts/Bodoni-Moda-OFL.txt");
  });

  it("scopes Bodoni to the wordmark: its token has exactly one consumer, and it is not in the body stack", () => {
    expect(themeDecls().get("--font-wordmark")).toMatch(/^"Bodoni Moda",/);
    expect(themeDecls().get("--font-sans")).not.toMatch(/bodoni/i);
    const consumers = Object.entries(appSources)
      .filter(([p, src]) => !p.endsWith("/index.css") && /font-wordmark|--font-wordmark|bodoni/i.test(src))
      .map(([p]) => p);
    expect(consumers).toEqual(["/src/shared/components/Wordmark.tsx"]);
  });

  it("renders the wordmark in the wordmark face at weight 700, and callers cannot override the family", () => {
    render(<Wordmark className="text-2xl font-sans" />);
    const mark = screen.getByText("TattooOS");
    // cn() is tailwind-merge: the component's own classes come last, so they win the conflict.
    expect(mark.className).toContain("font-wordmark");
    expect(mark.className).not.toMatch(/\bfont-sans\b/);
    expect(mark.className).toContain("font-bold");
    expect(mark.className).toContain("text-2xl");
  });
});

describe("radius (slice 5)", () => {
  it("puts the whole radius scale the app uses on the 3px baseline", () => {
    const t = themeDecls();
    for (const token of ["--radius", "--radius-sm", "--radius-md", "--radius-lg", "--radius-xl"]) {
      expect(t.get(token), token).toBe("3px");
    }
    // The toast variable derives from --radius, so toasts follow.
    expect(css).toContain("--border-radius: var(--radius);");
  });
});

describe("breakpoints (slice 5)", () => {
  it("adds the four-role page breakpoints beside the shell's, without redefining sm/md/lg", () => {
    const t = themeDecls();
    expect(t.get("--breakpoint-page-sm")).toBe("26.25rem"); // 420px
    expect(t.get("--breakpoint-page-md")).toBe("50rem"); // 800px
    expect(t.get("--breakpoint-page-lg")).toBe("68.75rem"); // 1100px
    // docs/03: keep the existing shared shell. sm/md/lg stay Tailwind's 640/768/1024, which the
    // shell's JS also hard-codes (shouldOpenNavDrawerForTourStep, OnboardingTour).
    for (const token of ["--breakpoint-sm", "--breakpoint-md", "--breakpoint-lg", "--breakpoint-xl"]) {
      expect(t.has(token), token).toBe(false);
    }
    // The token JSON's legacy 720/1050 must not appear as a breakpoint.
    expect(css).not.toMatch(/--breakpoint-[\w-]+:\s*(45rem|65\.625rem|720px|1050px)/);
  });
});
