import { useState } from "react";
import type { MonthlyRevenuePoint } from "../report.types";

const W     = 640;
const H     = 160;
const PAD_L = 60;
const PAD_R = 12;
const PAD_T = 12;
const PAD_B = 28;
const PW    = W - PAD_L - PAD_R;
const PH    = H - PAD_T - PAD_B;

function xAt(i: number, n: number) {
  return PAD_L + (n < 2 ? PW / 2 : (i / (n - 1)) * PW);
}

function yAt(val: number, max: number) {
  if (max === 0) return PAD_T + PH;
  return PAD_T + PH - (val / max) * PH;
}

function fmtY(val: number) {
  if (val >= 1000) return `€${(val / 1000).toFixed(1)}k`;
  return `€${Math.round(val)}`;
}

function fmtMonth(iso: string) {
  const [year, month] = iso.split("-").map(Number);
  const d = new Date(year, month - 1, 1);
  return d.toLocaleString("en-GB", { month: "short" });
}

interface TooltipData {
  x:       number;
  y:       number;
  revenue: number;
  month:   string;
}

// Hand-rolled inline SVG chart — same treatment as MrrChart.tsx's Chart component
// (no charting library is used anywhere in this codebase; recharts is not installed).
export function RevenueTrendChart({ data }: { data: MonthlyRevenuePoint[] }) {
  const [tooltip, setTooltip] = useState<TooltipData | null>(null);

  const n     = data.length;
  const max   = Math.max(...data.map((d) => d.revenue), 1);
  const gridY = [0, 0.5, 1].map((t) => ({ val: max * t, y: yAt(max * t, max) }));

  const points = data.map((d, i) => ({ x: xAt(i, n), y: yAt(d.revenue, max), d }));
  const linePts = points.map((p) => `${p.x},${p.y}`).join(" ");

  const areaPath = n > 0 ? [
    `M ${points[0].x} ${PAD_T + PH}`,
    ...points.map((p) => `L ${p.x} ${p.y}`),
    `L ${points[n - 1].x} ${PAD_T + PH}`,
    "Z",
  ].join(" ") : "";

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width="100%" aria-label="Revenue trend" role="img">
      <text
        x={10} y={PAD_T + PH / 2} textAnchor="middle" fontSize={8}
        fill="currentColor" fillOpacity={0.4}
        transform={`rotate(-90, 10, ${PAD_T + PH / 2})`}
      >
        EUR
      </text>

      {gridY.map(({ val, y }) => (
        <g key={val}>
          <line x1={PAD_L} y1={y} x2={W - PAD_R} y2={y} stroke="currentColor" strokeOpacity={0.12} strokeWidth={1} />
          <text x={PAD_L - 6} y={y} dominantBaseline="middle" textAnchor="end" fontSize={9}
                fill="currentColor" fillOpacity={0.5}>
            {fmtY(val)}
          </text>
        </g>
      ))}

      {n > 0 && <path d={areaPath} style={{ fill: "hsl(var(--primary) / 0.08)" }} />}

      {n > 0 && (
        <polyline
          points={linePts} fill="none" style={{ stroke: "hsl(var(--primary))" }}
          strokeWidth={2} strokeLinejoin="round" strokeLinecap="round"
        />
      )}

      {points.map(({ x, y, d }, i) => (
        <g
          key={d.month}
          onMouseEnter={() => setTooltip({ x, y, revenue: d.revenue, month: d.month })}
          onMouseLeave={() => setTooltip(null)}
          style={{ cursor: "default" }}
        >
          <circle cx={x} cy={y} r={4} style={{ fill: "hsl(var(--primary))" }} />
          <circle cx={x} cy={y} r={10} fill="transparent" />
          {(i % 2 === 0 || i === n - 1) && (
            <text x={x} y={H - 4} textAnchor="middle" fontSize={9} fill="currentColor" fillOpacity={0.5}>
              {fmtMonth(d.month)}
            </text>
          )}
        </g>
      ))}

      {tooltip && (() => {
        const tipW = 84;
        const tipH = 28;
        const tipX = Math.min(Math.max(tooltip.x - tipW / 2, PAD_L), W - PAD_R - tipW);
        const tipY = tooltip.y - tipH - 6;
        return (
          <g>
            <rect x={tipX} y={tipY} width={tipW} height={tipH} rx={4}
                  fill="hsl(var(--popover))" stroke="hsl(var(--border))" strokeWidth={0.5} />
            <text x={tipX + tipW / 2} y={tipY + 10} textAnchor="middle" fontSize={9}
                  fill="currentColor" fillOpacity={0.7}>
              {fmtMonth(tooltip.month)}
            </text>
            <text x={tipX + tipW / 2} y={tipY + 21} textAnchor="middle" fontSize={10}
                  fontWeight="600" fill="currentColor">
              {fmtY(tooltip.revenue)}
            </text>
          </g>
        );
      })()}
    </svg>
  );
}
