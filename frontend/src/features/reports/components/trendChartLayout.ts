/** Room one month label needs ("Sep" at 11px is ~20px) plus a gap, so neighbours never touch. */
export const MIN_LABEL_SPACING = 36;

/**
 * Which months get an x-axis label for a plot `plotWidth` pixels wide. Counted back from the newest month,
 * which is always labelled, every `step`-th one where `step` keeps labels at least MIN_LABEL_SPACING apart.
 * Every point is still drawn and has its tooltip and title; only the axis text thins out.
 */
export function monthLabelIndexes(n: number, plotWidth: number): Set<number> {
  if (n <= 0) return new Set();
  const gap = n < 2 ? Infinity : plotWidth / (n - 1);
  const step = Math.max(1, Math.ceil(MIN_LABEL_SPACING / gap));
  const shown = new Set<number>();
  for (let i = n - 1; i >= 0; i -= step) shown.add(i);
  return shown;
}
