// Shared helpers for the colour-contrast tests (toastTheme, hard-coded text colour guard).
// WCAG 2.1 relative luminance and contrast ratio: https://www.w3.org/TR/WCAG21/#dfn-relative-luminance

export type Rgb = [number, number, number];

const channel = (c: number) => { const s = c / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
const luminance = ([r, g, b]: Rgb) => 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);

export function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** A translucent layer painted over an opaque backdrop (straight sRGB blend, as the browser paints it). */
export const over = (fg: Rgb, alpha: number, bg: Rgb): Rgb =>
  [0, 1, 2].map((i) => Math.round(fg[i] * alpha + bg[i] * (1 - alpha))) as Rgb;

export function hexToRgb(value: string): Rgb {
  const m = /^#([0-9a-f]{6})$/i.exec(value.trim());
  if (!m) throw new Error(`expected a #RRGGBB value, got "${value}"`);
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

/** CSS Color 4 hsl() → sRGB, rounded the way Chromium serialises it. */
export function hslToRgb(value: string): Rgb {
  const m = /^hsl\(\s*([\d.]+)\s+([\d.]+)%\s+([\d.]+)%\s*\)$/.exec(value.trim());
  if (!m) throw new Error(`expected hsl(h s% l%), got "${value}"`);
  const h = Number(m[1]), s = Number(m[2]) / 100, l = Number(m[3]) / 100;
  const a = s * Math.min(l, 1 - l);
  const f = (n: number) => { const k = (n + h / 30) % 12; return l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)); };
  return [f(0), f(8), f(4)].map((v) => Math.round(v * 255)) as Rgb;
}

export function parseColour(value: string): Rgb {
  return value.trim().startsWith("#") ? hexToRgb(value) : hslToRgb(value);
}

/** Tailwind v4's palette is written in oklch(); convert to sRGB with the CSS Color 4 OKLab
 *  matrices and per-channel clipping (matches what Chromium paints and axe-core reads). */
export function oklchToRgb(L: number, C: number, H: number): Rgb {
  const a = C * Math.cos((H * Math.PI) / 180), b = C * Math.sin((H * Math.PI) / 180);
  const l_ = L + 0.3963377774 * a + 0.2158037573 * b;
  const m_ = L - 0.1055613458 * a - 0.0638541728 * b;
  const s_ = L - 0.0894841775 * a - 1.291485548 * b;
  const l = l_ ** 3, m = m_ ** 3, s = s_ ** 3;
  const lin = [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ];
  return lin.map((v) => {
    const c = Math.min(1, Math.max(0, v));
    const g = c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055;
    return Math.round(Math.min(1, Math.max(0, g)) * 255);
  }) as Rgb;
}

/** `--color-green-600: oklch(62.7% 0.194 149.214);` → { "green-600": [r, g, b], … } */
export function parseTailwindPalette(themeCss: string): Record<string, Rgb> {
  const palette: Record<string, Rgb> = {};
  for (const m of themeCss.matchAll(/--color-([a-z]+-\d{2,3}):\s*oklch\(([\d.]+)%\s+([\d.]+)\s+([\d.]+)\)/g)) {
    palette[m[1]] = oklchToRgb(Number(m[2]) / 100, Number(m[3]), Number(m[4]));
  }
  return palette;
}

// ── Tiny CSS block parser (comments stripped, braces matched) ─────────────────────────────

const norm = (s: string) => s.replace(/\s+/g, " ").trim();

export type CssBlock = { path: string[]; decls: Map<string, string> };

export function parseCssBlocks(css: string): CssBlock[] {
  const src = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const blocks: CssBlock[] = [];
  const stack: string[] = [];
  let buf = "";
  for (const ch of src) {
    if (ch === "{") {
      stack.push(norm(buf.slice(buf.lastIndexOf(";") + 1)));
      buf = "";
    } else if (ch === "}") {
      const header = stack.pop();
      if (header === undefined) throw new Error("unbalanced braces");
      const decls = new Map<string, string>();
      for (const part of buf.split(";")) {
        const i = part.indexOf(":");
        if (i > 0) decls.set(part.slice(0, i).trim(), norm(part.slice(i + 1)));
      }
      blocks.push({ path: [...stack, header], decls });
      buf = "";
    } else {
      buf += ch;
    }
  }
  return blocks;
}
