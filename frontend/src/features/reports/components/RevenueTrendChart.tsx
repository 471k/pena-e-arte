import { useEffect, useRef, useState } from "react";
import { currencyLabel } from "@/shared/utils/formatCurrency";
import type { MonthlyRevenuePoint } from "../report.types";
import { monthLabelIndexes } from "./trendChartLayout";

// The drawing is laid out in real pixels at whatever width its container has, so 1 SVG unit is 1 CSS pixel
// and the text is the size written here at every width. It used to be a fixed 640-wide viewBox scaled to
// fit: on a 390px phone the whole drawing, labels included, shrank to about half size (E05b-1, finding 4).
const DEFAULT_W  = 640;
const H          = 168;
const PAD_L      = 60;
const PAD_R      = 12;
const PAD_T      = 12;
const PAD_B      = 30;
const PH         = H - PAD_T - PAD_B;
const LABEL_FONT = 11;
const TITLE_FONT = 10;

function xAt(i: number, n: number, pw: number) {
  return PAD_L + (n < 2 ? pw / 2 : (i / (n - 1)) * pw);
}

function yAt(val: number, max: number) {
  if (max === 0) return PAD_T + PH;
  return PAD_T + PH - (val / max) * PH;
}

function fmtY(val: number, currency: string) {
  const symbol = currencyLabel(currency);
  if (val >= 1000) return `${symbol}${(val / 1000).toFixed(1)}k`;
  return `${symbol}${Math.round(val)}`;
}

function fmtMonth(iso: string) {
  const [year, month] = iso.split("-").map(Number);
  const d = new Date(year, month - 1, 1);
  return d.toLocaleString("en-GB", { month: "short" });
}

function fmtMonthYear(iso: string) {
  const [year, month] = iso.split("-").map(Number);
  return new Date(year, month - 1, 1).toLocaleString("en-GB", { month: "short", year: "numeric" });
}

interface TooltipData {
  x:       number;
  y:       number;
  revenue: number;
  month:   string;
}

// Hand-rolled inline SVG chart — same treatment as MrrChart.tsx's Chart component
// (no charting library is used anywhere in this codebase; recharts is not installed).
export function RevenueTrendChart({ data, currency }: { data: MonthlyRevenuePoint[]; currency: string }) {
  const [tooltip, setTooltip] = useState<TooltipData | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(DEFAULT_W);

  // Follow the container's width (a layout subscription, not data fetching).
  useEffect(() => {
    const el = containerRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver((entries) => {
      const w = Math.round(entries[0]?.contentRect.width ?? 0);
      if (w > 0) setWidth(w);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const W  = width;
  const PW = Math.max(1, W - PAD_L - PAD_R);
  const n     = data.length;
  const max   = Math.max(...data.map((d) => d.revenue), 1);
  const gridY = [0, 0.5, 1].map((t) => ({ val: max * t, y: yAt(max * t, max) }));
  const labelled = monthLabelIndexes(n, PW);

  const points = data.map((d, i) => ({ x: xAt(i, n, PW), y: yAt(d.revenue, max), d }));
  const linePts = points.map((p) => `${p.x},${p.y}`).join(" ");

  const areaPath = n > 0 ? [
    `M ${points[0].x} ${PAD_T + PH}`,
    ...points.map((p) => `L ${p.x} ${p.y}`),
    `L ${points[n - 1].x} ${PAD_T + PH}`,
    "Z",
  ].join(" ") : "";

  return (
    <div ref={containerRef} className="w-full">
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" height={H} aria-label="Revenue trend" role="img">
        <text
          x={10} y={PAD_T + PH / 2} textAnchor="middle" fontSize={TITLE_FONT}
          fill="currentColor" fillOpacity={0.55}
          transform={`rotate(-90, 10, ${PAD_T + PH / 2})`}
        >
          {currency}
        </text>

        {gridY.map(({ val, y }) => (
          <g key={val}>
            <line x1={PAD_L} y1={y} x2={W - PAD_R} y2={y} stroke="currentColor" strokeOpacity={0.12} strokeWidth={1} />
            <text x={PAD_L - 6} y={y} dominantBaseline="middle" textAnchor="end" fontSize={LABEL_FONT}
                  fill="currentColor" fillOpacity={0.65}>
              {fmtY(val, currency)}
            </text>
          </g>
        ))}

        {n > 0 && <path d={areaPath} style={{ fill: "color-mix(in srgb, var(--color-primary) 8%, transparent)" }} />}

        {n > 0 && (
          <polyline
            points={linePts} fill="none" style={{ stroke: "var(--color-primary)" }}
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
            <title>{`${fmtMonthYear(d.month)}: ${fmtY(d.revenue, currency)}`}</title>
            <circle cx={x} cy={y} r={4} style={{ fill: "var(--color-primary)" }} />
            <circle cx={x} cy={y} r={10} fill="transparent" />
            {labelled.has(i) && (
              <text x={x} y={H - 8} textAnchor="middle" fontSize={LABEL_FONT} fill="currentColor" fillOpacity={0.65}
                    data-testid="trend-month-label">
                {fmtMonth(d.month)}
              </text>
            )}
          </g>
        ))}

        {tooltip && (() => {
          const tipW = 92;
          const tipH = 32;
          const tipX = Math.min(Math.max(tooltip.x - tipW / 2, PAD_L), W - PAD_R - tipW);
          const tipY = Math.max(0, tooltip.y - tipH - 6);
          return (
            <g>
              <rect x={tipX} y={tipY} width={tipW} height={tipH} rx={4}
                    fill="var(--color-popover)" stroke="var(--color-border)" strokeWidth={0.5} />
              <text x={tipX + tipW / 2} y={tipY + 12} textAnchor="middle" fontSize={10}
                    fill="currentColor" fillOpacity={0.75}>
                {fmtMonth(tooltip.month)}
              </text>
              <text x={tipX + tipW / 2} y={tipY + 25} textAnchor="middle" fontSize={12}
                    fontWeight="600" fill="currentColor">
                {fmtY(tooltip.revenue, currency)}
              </text>
            </g>
          );
        })()}
      </svg>
    </div>
  );
}
