import { useState } from "react";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMrrMovementsQuery } from "../platformApi";
import type { MrrMovementsDataPoint } from "../platform.types";

const W      = 480;
const H      = 150;
const PAD_L  = 60;
const PAD_R  = 12;
const PAD_T  = 12;
const PAD_B  = 28;
const PW     = W - PAD_L - PAD_R;
const PH     = H - PAD_T - PAD_B;

// Upward segments stack in shades of the success accent, downward ones in the danger accent.
// Each pairs a light-mode and a dark-mode fill so the segments stay distinguishable in both.
const SEGMENTS = [
  { key: "new",          label: "New",          cls: "fill-emerald-700 dark:fill-emerald-600", swatch: "bg-emerald-700 dark:bg-emerald-600" },
  { key: "expansion",    label: "Expansion",    cls: "fill-emerald-500 dark:fill-emerald-500", swatch: "bg-emerald-500" },
  { key: "reactivation", label: "Reactivation", cls: "fill-emerald-300 dark:fill-emerald-400", swatch: "bg-emerald-300 dark:bg-emerald-400" },
  { key: "contraction",  label: "Contraction",  cls: "fill-red-400 dark:fill-red-400",         swatch: "bg-red-400" },
  { key: "churn",        label: "Churn",        cls: "fill-red-700 dark:fill-red-600",         swatch: "bg-red-700 dark:bg-red-600" },
] as const;

function fmtEuro(val: number) {
  const abs = Math.abs(val);
  const sign = val < 0 ? "-" : "";
  if (abs >= 1000) return `${sign}€${(abs / 1000).toFixed(1)}k`;
  return `${sign}€${Math.round(abs)}`;
}

function fmtEuroExact(val: number) {
  return `${val < 0 ? "-" : ""}€${Math.abs(val).toFixed(2)}`;
}

function fmtMonth(iso: string) {
  const [year, month] = iso.split("-").map(Number);
  return new Date(year, month - 1, 1).toLocaleString("en-GB", { month: "short" });
}

function hasAnyMovement(data: MrrMovementsDataPoint[]): boolean {
  return data.some(
    (d) => d.new !== 0 || d.expansion !== 0 || d.reactivation !== 0 || d.contraction !== 0 || d.churn !== 0,
  );
}

function Chart({ data }: { data: MrrMovementsDataPoint[] }) {
  const n = data.length;
  const positives = data.map((d) => d.new + d.expansion + d.reactivation);
  const negatives = data.map((d) => Math.abs(d.contraction + d.churn));
  const netExtent = data.map((d) => d.net);

  // The scale must contain every stack AND the net line, which can sit outside both.
  const maxUp   = Math.max(...positives, ...netExtent, 1);
  const maxDown = Math.max(...negatives, ...netExtent.map((v) => -v), 0);
  const span    = maxUp + maxDown;

  const zeroY  = PAD_T + (maxUp / span) * PH;
  const scale  = PH / span;
  const slotW  = PW / n;
  const barW   = Math.min(slotW * 0.55, 36);
  const xMid   = (i: number) => PAD_L + slotW * i + slotW / 2;

  const netPoints = data.map((d, i) => `${xMid(i)},${zeroY - d.net * scale}`).join(" ");

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width="100%" aria-label="MRR movements by month" role="img">
      {/* y-axis EUR label */}
      <text
        x={10} y={PAD_T + PH / 2} textAnchor="middle" fontSize={8}
        fill="currentColor" fillOpacity={0.4}
        transform={`rotate(-90, 10, ${PAD_T + PH / 2})`}
      >
        EUR
      </text>

      {/* zero baseline + top/bottom scale labels */}
      <line x1={PAD_L} y1={zeroY} x2={W - PAD_R} y2={zeroY} stroke="currentColor" strokeOpacity={0.3} strokeWidth={1} />
      <text x={PAD_L - 6} y={zeroY} dominantBaseline="middle" textAnchor="end" fontSize={9} fill="currentColor" fillOpacity={0.5}>
        €0
      </text>
      <text x={PAD_L - 6} y={PAD_T} dominantBaseline="middle" textAnchor="end" fontSize={9} fill="currentColor" fillOpacity={0.5}>
        {fmtEuro(maxUp)}
      </text>
      {maxDown > 0 && (
        <text x={PAD_L - 6} y={PAD_T + PH} dominantBaseline="middle" textAnchor="end" fontSize={9} fill="currentColor" fillOpacity={0.5}>
          {fmtEuro(-maxDown)}
        </text>
      )}

      {data.map((d, i) => {
        const x = xMid(i) - barW / 2;
        let up = zeroY;
        let down = zeroY;
        const rects: { key: string; cls: string; y: number; h: number }[] = [];

        for (const seg of SEGMENTS) {
          const value = d[seg.key];
          if (value === 0) continue;
          const h = Math.abs(value) * scale;
          if (value > 0) {
            up -= h;
            rects.push({ key: seg.key, cls: seg.cls, y: up, h });
          } else {
            rects.push({ key: seg.key, cls: seg.cls, y: down, h });
            down += h;
          }
        }

        return (
          <g key={d.month}>
            <title>
              {`${d.month} — New ${fmtEuroExact(d.new)}, Expansion ${fmtEuroExact(d.expansion)}, ` +
                `Reactivation ${fmtEuroExact(d.reactivation)}, Contraction ${fmtEuroExact(d.contraction)}, ` +
                `Churn ${fmtEuroExact(d.churn)}, Net ${fmtEuroExact(d.net)}`}
            </title>
            {rects.map((r) => (
              <rect key={r.key} x={x} y={r.y} width={barW} height={r.h} className={r.cls} />
            ))}
            {/* Larger invisible hit area so the <title> tooltip is easy to reach */}
            <rect x={PAD_L + slotW * i} y={PAD_T} width={slotW} height={PH} fill="transparent" />
            {(i % 2 === 0 || i === n - 1) && (
              <text x={xMid(i)} y={H - 4} textAnchor="middle" fontSize={9} fill="currentColor" fillOpacity={0.5}>
                {fmtMonth(d.month)}
              </text>
            )}
          </g>
        );
      })}

      {/* net line overlay */}
      {n > 1 && (
        <polyline
          points={netPoints}
          fill="none"
          style={{ stroke: "hsl(var(--primary))" }}
          strokeWidth={2}
          strokeLinejoin="round"
          strokeLinecap="round"
        />
      )}
      {data.map((d, i) => (
        <circle key={d.month} cx={xMid(i)} cy={zeroY - d.net * scale} r={3} style={{ fill: "hsl(var(--primary))" }} />
      ))}
    </svg>
  );
}

export function MrrMovementsChart() {
  const [months, setMonths] = useState<3 | 6 | 12>(6);
  const { data, isLoading, isError } = useGetMrrMovementsQuery(months);

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between gap-2">
          <CardTitle className="text-sm">MRR movements</CardTitle>
          <div className="flex items-center gap-1">
            {([3, 6, 12] as const).map((m) => (
              <button
                key={m}
                onClick={() => setMonths(m)}
                aria-pressed={months === m}
                className={`text-[11px] px-2 py-0.5 rounded transition-colors ${
                  months === m
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:text-foreground hover:bg-muted"
                }`}
              >
                {m}m
              </button>
            ))}
          </div>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        {isLoading ? (
          <Skeleton className="h-[150px] w-full" />
        ) : isError ? (
          <p className="h-[150px] flex items-center justify-center text-xs text-destructive-text" role="alert">
            Couldn't load MRR movements — try refreshing.
          </p>
        ) : !data || !hasAnyMovement(data) ? (
          <p className="h-[150px] flex items-center justify-center text-xs text-muted-foreground text-center px-4">
            No revenue movements recorded yet. Movements are recorded from the moment a subscription changes.
          </p>
        ) : (
          <>
            <Chart data={data} />
            <ul className="flex flex-wrap gap-x-3 gap-y-1 mt-1" aria-label="Legend">
              {SEGMENTS.map((seg) => (
                <li key={seg.key} className="flex items-center gap-1 text-[10px] text-muted-foreground">
                  <span aria-hidden="true" className={`inline-block h-2 w-2 rounded-sm ${seg.swatch}`} />
                  {seg.label}
                </li>
              ))}
              <li className="flex items-center gap-1 text-[10px] text-muted-foreground">
                <span aria-hidden="true" className="inline-block h-0.5 w-3 bg-primary" />
                Net
              </li>
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}
