import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { STATIC_ROUTES } from "@/shared/seo/siteRoutes";

const FRONTEND_DIR = resolve(import.meta.dirname, "../..");
const TEMPLATE = readFileSync(join(FRONTEND_DIR, "index.html"), "utf8");

function runScript(distDir: string) {
  return spawnSync(process.execPath, ["scripts/prerender-meta.ts", distDir], {
    cwd: FRONTEND_DIR,
    encoding: "utf8",
  });
}

describe("scripts/prerender-meta.ts", () => {
  let dist: string;

  beforeEach(() => {
    dist = mkdtempSync(join(tmpdir(), "prerender-"));
  });
  afterEach(() => rmSync(dist, { recursive: true, force: true }));

  it("writes one HTML file per static route, nested paths included, each with its own title", () => {
    writeFileSync(join(dist, "index.html"), TEMPLATE, "utf8");

    const result = runScript(dist);

    expect(result.status, result.stderr).toBe(0);
    for (const route of STATIC_ROUTES) {
      const file = join(dist, `${route.path.slice(1)}.html`);
      expect(existsSync(file), route.path).toBe(true);
      const html = readFileSync(file, "utf8");
      expect(html).toContain(`<title>${route.title.replace(/&/g, "&amp;")}</title>`);
      expect(html).toContain(`rel="canonical" href="https://tattooos.co${route.path}"`);
    }
    expect(existsSync(join(dist, "use", "booking.html"))).toBe(true);
  });

  it("does not modify index.html itself", () => {
    writeFileSync(join(dist, "index.html"), TEMPLATE, "utf8");

    runScript(dist);

    expect(readFileSync(join(dist, "index.html"), "utf8")).toBe(TEMPLATE);
  });

  it("exits non-zero when the head markers are missing from index.html", () => {
    writeFileSync(join(dist, "index.html"), "<html><head><title>x</title></head><body></body></html>", "utf8");

    const result = runScript(dist);

    expect(result.status).toBe(1);
    expect(result.stderr).toMatch(/markers/);
  });

  it("exits non-zero when there is no index.html to start from", () => {
    mkdirSync(join(dist, "empty"));

    const result = runScript(join(dist, "empty"));

    expect(result.status).toBe(1);
    expect(result.stderr).toMatch(/not found/);
  });
});
