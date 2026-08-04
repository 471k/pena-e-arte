const W = 480, H = 130, PAD_L = 40, PAD_R = 12, PAD_T = 12, PAD_B = 24;
const PLOT_W = W - PAD_L - PAD_R, PLOT_H = H - PAD_T - PAD_B;

interface LineAreaChartProps<T> {
  data: T[];
  valueOf: (d: T) => number;
  /** Return a string to render an x-axis label under this point, or null to skip it. */
  labelOf: (d: T, index: number, total: number) => string | null;
  ariaLabel: string;
}

/**
 * Minimal hand-rolled inline-SVG line/area chart — no charting library used anywhere in this
 * codebase. Extracted so a third feature needing a simple trend line (after MrrChart.tsx and
 * RevenueTrendChart.tsx) doesn't have to re-derive the same axis math a third time. MrrChart/
 * RevenueTrendChart have their own richer variants (hover tooltip, gridlines) not yet migrated
 * onto this shared component — flagged as a known follow-up, not silently left inconsistent.
 */
export function LineAreaChart<T>({ data, valueOf, labelOf, ariaLabel }: LineAreaChartProps<T>) {
  const n = data.length;
  const max = Math.max(...data.map(valueOf), 1);

  const xAt = (i: number) => PAD_L + (n < 2 ? PLOT_W / 2 : (i / (n - 1)) * PLOT_W);
  const yAt = (v: number) => PAD_T + PLOT_H - (v / max) * PLOT_H;

  const points = data.map((d, i) => ({ x: xAt(i), y: yAt(valueOf(d)), d, i }));
  const linePts = points.map((p) => `${p.x},${p.y}`).join(" ");
  const areaPath = [
    `M ${points[0].x} ${PAD_T + PLOT_H}`,
    ...points.map((p) => `L ${p.x} ${p.y}`),
    `L ${points[n - 1].x} ${PAD_T + PLOT_H}`,
    "Z",
  ].join(" ");

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width="100%" aria-label={ariaLabel} role="img">
      <line x1={PAD_L} y1={PAD_T + PLOT_H} x2={W - PAD_R} y2={PAD_T + PLOT_H}
            stroke="currentColor" strokeOpacity={0.12} strokeWidth={1} />
      <path d={areaPath} style={{ fill: "hsl(var(--primary) / 0.08)" }} />
      <polyline points={linePts} fill="none" style={{ stroke: "hsl(var(--primary))" }}
                strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
      {points.map(({ x, y, d, i }) => {
        const label = labelOf(d, i, n);
        return (
          <g key={i}>
            <circle cx={x} cy={y} r={3} style={{ fill: "hsl(var(--primary))" }} />
            {label && (
              <text x={x} y={H - 4} textAnchor="middle" fontSize={9} fill="currentColor" fillOpacity={0.5}>
                {label}
              </text>
            )}
          </g>
        );
      })}
    </svg>
  );
}
