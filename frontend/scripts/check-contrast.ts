/**
 * Computes WCAG relative-luminance contrast ratios for the design-token pairs that
 * must stay readable/perceivable in both themes, and fails (non-zero exit) if any
 * pair drops below its required threshold. Run with `node scripts/check-contrast.ts`
 * (or `pnpm check-contrast`) — Node 22.6+ runs .ts files directly, no build step.
 *
 * Token values below are hand-kept in sync with frontend/src/index.css. If you
 * change a --color-* token there, update the matching entry here.
 */

type Hsl = { h: number; s: number; l: number };

function hslToRgb({ h, s, l }: Hsl): [number, number, number] {
  const sFrac = s / 100;
  const lFrac = l / 100;
  const k = (n: number) => (n + h / 30) % 12;
  const a = sFrac * Math.min(lFrac, 1 - lFrac);
  const f = (n: number) => lFrac - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return [f(0) * 255, f(8) * 255, f(4) * 255];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const linear = (c: number) => {
    const cs = c / 255;
    return cs <= 0.03928 ? cs / 12.92 : Math.pow((cs + 0.055) / 1.055, 2.4);
  };
  const [rl, gl, bl] = [linear(r), linear(g), linear(b)];
  return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
}

function contrastRatio(a: Hsl, b: Hsl): number {
  const la = relativeLuminance(hslToRgb(a));
  const lb = relativeLuminance(hslToRgb(b));
  const lighter = Math.max(la, lb);
  const darker = Math.min(la, lb);
  return (lighter + 0.05) / (darker + 0.05);
}

// Mirrors frontend/src/index.css.
const light = {
  background: { h: 0, s: 0, l: 100 },
  border: { h: 240, s: 5.9, l: 58 },
  input: { h: 240, s: 5.9, l: 58 },
  destructiveText: { h: 0, s: 74, l: 42 },
};

const dark = {
  background: { h: 240, s: 10, l: 3.9 },
  border: { h: 240, s: 5, l: 40 },
  input: { h: 240, s: 5, l: 40 },
  destructiveText: { h: 0, s: 90, l: 65 },
};

type Check = {
  theme: "light" | "dark";
  pair: string;
  ratio: number;
  threshold: number;
};

const checks: Check[] = [
  { theme: "light", pair: "border / background", ratio: contrastRatio(light.border, light.background), threshold: 3.0 },
  { theme: "light", pair: "input / background", ratio: contrastRatio(light.input, light.background), threshold: 3.0 },
  { theme: "light", pair: "destructive-text / background", ratio: contrastRatio(light.destructiveText, light.background), threshold: 4.5 },
  { theme: "dark", pair: "border / background", ratio: contrastRatio(dark.border, dark.background), threshold: 3.0 },
  { theme: "dark", pair: "input / background", ratio: contrastRatio(dark.input, dark.background), threshold: 3.0 },
  { theme: "dark", pair: "destructive-text / background", ratio: contrastRatio(dark.destructiveText, dark.background), threshold: 4.5 },
];

let allPass = true;

console.log("theme  pair                              ratio    threshold  result");
console.log("-----  --------------------------------  -------  ---------  ------");
for (const c of checks) {
  const pass = c.ratio >= c.threshold;
  if (!pass) allPass = false;
  console.log(
    `${c.theme.padEnd(5)}  ${c.pair.padEnd(34)}  ${c.ratio.toFixed(2).padStart(5)}:1  ${(c.threshold + ":1").padEnd(9)}  ${pass ? "PASS" : "FAIL"}`
  );
}

console.log();
if (!allPass) {
  console.error("One or more token pairs are below the required WCAG contrast ratio.");
  process.exit(1);
}
console.log("All token pairs meet their required WCAG contrast ratio.");
