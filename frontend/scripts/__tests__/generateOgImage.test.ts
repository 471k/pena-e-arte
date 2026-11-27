import { describe, it, expect, afterEach } from "vitest";
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { execFileSync } from "node:child_process";
import {
  EXPECTED_HEIGHT,
  EXPECTED_WIDTH,
  SOURCE,
  TARGET,
  pngSize,
  sha256,
  syncOgImage,
  verifyOgImage,
} from "../generate-og-image.mjs";

// Brand master 1.0.0 (27 Nov 2026): the script that used to DRAW a placeholder over public/og-image.png
// now copies the committed master and verifies it. These tests read and write real files, so they live
// here with the other Node-API script tests, outside the app's tsconfig scope.

/** The hash deployment-map.json gives share/link-preview.png in brand master 1.0.0. */
const MASTER_SHA256 = "11aa25b77ccef336219ea0241c38cdf6326f886e19ebf11fc1feedeb6d886fb8";

const tempDirs: string[] = [];
function tempDir(): string {
  const dir = mkdtempSync(join(tmpdir(), "og-image-test-"));
  tempDirs.push(dir);
  return dir;
}

afterEach(() => {
  for (const dir of tempDirs.splice(0)) rmSync(dir, { recursive: true, force: true });
});

describe("generate-og-image.mjs (copy and verify)", () => {
  it("the committed source and public/og-image.png are both the brand master, 1200x630", () => {
    const source = readFileSync(SOURCE);
    const target = readFileSync(TARGET);
    expect(sha256(source)).toBe(MASTER_SHA256);
    expect(sha256(target)).toBe(MASTER_SHA256);
    expect(pngSize(target)).toEqual({ width: EXPECTED_WIDTH, height: EXPECTED_HEIGHT });
    expect(verifyOgImage(source, target).problems).toEqual([]);
  });

  it("copies the source over a stale target and then verifies clean", () => {
    const dir = tempDir();
    const target = join(dir, "og-image.png");
    writeFileSync(target, "an old placeholder");
    const result = syncOgImage({ source: SOURCE, target });
    expect(result.problems).toEqual([]);
    expect(sha256(readFileSync(target))).toBe(MASTER_SHA256);
  });

  it("--check mode changes nothing and reports a mismatched target", () => {
    const dir = tempDir();
    const target = join(dir, "og-image.png");
    writeFileSync(target, "an old placeholder");
    const result = syncOgImage({ source: SOURCE, target, checkOnly: true });
    expect(readFileSync(target, "utf8")).toBe("an old placeholder");
    expect(result.problems.some((p) => p.startsWith("hash mismatch"))).toBe(true);
    expect(result.problems.some((p) => p.includes("not a PNG"))).toBe(true);
  });

  it("rejects a PNG of the wrong size even when both copies agree", () => {
    // A minimal 1x1 PNG header: signature, IHDR length and type, width 1, height 1.
    const tiny = Buffer.alloc(33);
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).copy(tiny, 0);
    tiny.writeUInt32BE(13, 8);
    tiny.write("IHDR", 12, "latin1");
    tiny.writeUInt32BE(1, 16);
    tiny.writeUInt32BE(1, 20);
    expect(pngSize(tiny)).toEqual({ width: 1, height: 1 });
    expect(verifyOgImage(tiny, tiny).problems).toEqual([`og-image is 1x1, expected 1200x630`]);
  });

  it("as a CLI, exits non-zero on a mismatch and zero when the target is the master", () => {
    const script = resolve(import.meta.dirname, "../generate-og-image.mjs");
    // The real target is the master, so --check passes and prints the hash.
    const out = execFileSync(process.execPath, [script, "--check"], { encoding: "utf8" });
    expect(out).toContain(MASTER_SHA256);

    // A copy of the script beside a wrong target must fail. The script resolves its paths from its own
    // location, so build that layout in a temp dir.
    const dir = tempDir();
    const scriptsDir = join(dir, "scripts");
    const sourceDir = join(dir, "brand-assets", "after-hours-1.0.0");
    const publicDir = join(dir, "public");
    for (const d of [scriptsDir, sourceDir, publicDir]) mkdirSync(d, { recursive: true });
    copyFileSync(script, join(scriptsDir, "generate-og-image.mjs"));
    copyFileSync(SOURCE, join(sourceDir, "link-preview.png"));
    writeFileSync(join(publicDir, "og-image.png"), "wrong");
    let status = 0;
    try {
      execFileSync(process.execPath, [join(scriptsDir, "generate-og-image.mjs"), "--check"], { stdio: "pipe" });
    } catch (error) {
      status = (error as { status: number }).status;
    }
    expect(status).toBe(1);
  });
});
