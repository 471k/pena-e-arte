import { describe, it, expect } from "vitest";
import { HELP_ARTICLES, FAQ_ITEMS } from "../helpContent";
// `?raw` (declared by vite/client) returns the file's raw text content — avoids needing
// Node's `fs`/`path` (not in this project's browser-scoped tsconfig.app.json `types`)
// just to read a static file for a plain unit test. See src/shared/__tests__/manifest.test.ts
// for the same pattern.
import userManualHtml from "../../../../public/user-manual/index.html?raw";

// Drift guard (D1/D7) — these strings all describe the retired five-tier catalogue
// (Pro/"Professional") or the old hand-typed "save 17%" yearly-discount copy. Any hit
// here means Help or the manual fell out of sync with the current four-tier catalogue
// and its computed yearly saving (see architecture.md Decisions Log, "Four-tier
// catalogue + yearly on every paid tier").
const STALE_PATTERNS: RegExp[] = [
  /\bProfessional\b/,
  /Save 17%/,
  /€49\/mo/,
  /€490\/yr/,
  /\bPro plan\b/,
];

const helpContentText = JSON.stringify(HELP_ARTICLES) + JSON.stringify(FAQ_ITEMS);

describe("plan names stay in sync across docs", () => {
  it.each(STALE_PATTERNS)("helpContent.ts has no match for %s", (pattern) => {
    expect(helpContentText).not.toMatch(pattern);
  });

  it.each(STALE_PATTERNS)("the standalone user manual has no match for %s", (pattern) => {
    expect(userManualHtml).not.toMatch(pattern);
  });
});
