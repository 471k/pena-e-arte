# Fonts

Self-hosted webfonts for the app, declared by the `@font-face` block at the top of
`frontend/src/index.css`. Both families are licensed under the SIL Open Font License 1.1; the
licence files below travel with the fonts, as `docs/11` (assets and licensing) requires.

| File | Family | Weight | Used for | Licence |
|---|---|---|---|---|
| `dm-sans-400.woff2` | DM Sans | 400 | body text | `DM-Sans-OFL.txt` |
| `dm-sans-500.woff2` | DM Sans | 500 | medium labels | `DM-Sans-OFL.txt` |
| `dm-sans-600.woff2` | DM Sans | 600 | headings (a `font-bold` request also resolves here) | `DM-Sans-OFL.txt` |
| `bodoni-moda-700.woff2` | Bodoni Moda | 700 | the TattooOS wordmark only (`shared/components/Wordmark.tsx`) | `Bodoni-Moda-OFL.txt` |

**Source.** The After Hours engineering handoff package (2 Nov 2026), `prototype/assets/`
`font-4/5/6.ttf` (DM Sans 400/500/600) and `font-1.ttf` (Bodoni Moda 700). The family and weight
of each was read from the font's own `name` and `OS/2` tables rather than from the prototype's
CSS. The fonts are Latin subsets that include the Albanian ë/Ë and ç/Ç, plus €.

**Conversion.** A one-off `ttf -> woff2` with fontTools 4.55.3 + brotli 1.1.0 (`TTFont.flavor =
"woff2"`). Glyphs, outlines and names are unchanged, and neither licence declares a Reserved
Font Name. This is not a build step and adds no package to the repo.

**Only these faces.** `docs/03`'s type scale names DM Sans 400/500/600 and Bodoni Moda 700, so
the package's other faces (DM Sans 700, Bodoni 600/800/900) are deliberately not shipped. Add a
face here, with its `@font-face`, only when the scale names it. `src/shared/__tests__/typography.test.tsx`
fails on any other face, on a `.ttf`/`.otf` under `public/`, or on a second consumer of the wordmark face.
