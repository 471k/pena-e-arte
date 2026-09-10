import { describe, it, expect } from "vitest";
// `?raw` (declared by vite/client) returns the file's raw text content — avoids needing Node's
// `fs`/`path` (not in this project's browser-scoped tsconfig.app.json `types`) just to read a
// static asset for a plain unit test. Not a browser-level PWA/Lighthouse audit; this repo has no
// Lighthouse CI step to hook into.
import manifestRaw from "../../../public/manifest.json?raw";

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
});
