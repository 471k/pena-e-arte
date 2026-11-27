/**
 * Puts the approved default social-preview image in place and proves it is the approved one.
 *
 * frontend/public/og-image.png (1200x630) is the default share image referenced by index.html and the
 * per-route metadata (https://tattooos.co/og-image.png). Since brand master 1.0.0 (After Hours, 27 Nov
 * 2026) it is the package's mastered link preview, committed unchanged as
 * frontend/brand-assets/after-hours-1.0.0/link-preview.png. This script no longer DRAWS anything: it
 * copies that committed source over public/og-image.png, then checks that the two files' SHA-256 match
 * and that the PNG really is 1200x630 (read from its IHDR header, no image library). It exits non-zero
 * on any mismatch. Run manually; not part of `pnpm build`:
 *
 *   node scripts/generate-og-image.mjs           copy, then verify
 *   node scripts/generate-og-image.mjs --check   verify only, change nothing
 *
 * To change the artwork, commit a new approved source from a new brand master version and point
 * SOURCE at it. Never hand-edit public/og-image.png.
 */
import { createHash } from "node:crypto";
import { copyFileSync, readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));

export const SOURCE = join(here, "..", "brand-assets", "after-hours-1.0.0", "link-preview.png");
export const TARGET = join(here, "..", "public", "og-image.png");
export const EXPECTED_WIDTH = 1200;
export const EXPECTED_HEIGHT = 630;

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** Hex SHA-256 of a buffer. */
export function sha256(buffer) {
  return createHash("sha256").update(buffer).digest("hex");
}

/**
 * Width and height from a PNG's IHDR chunk, which the format requires to come first: the 8-byte
 * signature, then a 4-byte length, the type "IHDR", and width and height as big-endian uint32.
 * Throws if the buffer is not a PNG.
 */
export function pngSize(buffer) {
  if (buffer.length < 24 || !buffer.subarray(0, 8).equals(PNG_SIGNATURE)) throw new Error("not a PNG file");
  if (buffer.toString("latin1", 12, 16) !== "IHDR") throw new Error("PNG has no leading IHDR chunk");
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

/** Problems with the target compared with the source; an empty list means it is the approved image. */
export function verifyOgImage(sourceBuffer, targetBuffer) {
  const problems = [];
  const sourceHash = sha256(sourceBuffer);
  const targetHash = sha256(targetBuffer);
  if (sourceHash !== targetHash) problems.push(`hash mismatch: source ${sourceHash}, og-image ${targetHash}`);
  try {
    const { width, height } = pngSize(targetBuffer);
    if (width !== EXPECTED_WIDTH || height !== EXPECTED_HEIGHT) {
      problems.push(`og-image is ${width}x${height}, expected ${EXPECTED_WIDTH}x${EXPECTED_HEIGHT}`);
    }
  } catch (error) {
    problems.push(`og-image: ${error.message}`);
  }
  return { sourceHash, targetHash, problems };
}

/** Copies source over target unless checkOnly, then verifies. Returns the verification result. */
export function syncOgImage({ source = SOURCE, target = TARGET, checkOnly = false } = {}) {
  if (!checkOnly) copyFileSync(source, target);
  return verifyOgImage(readFileSync(source), readFileSync(target));
}

function main() {
  const checkOnly = process.argv.includes("--check");
  const { sourceHash, problems } = syncOgImage({ checkOnly });
  if (problems.length > 0) {
    for (const problem of problems) console.error(problem);
    process.exit(1);
  }
  console.log(`${checkOnly ? "verified" : "copied and verified"} ${TARGET}`);
  console.log(`sha256 ${sourceHash} (${EXPECTED_WIDTH}x${EXPECTED_HEIGHT})`);
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) main();
