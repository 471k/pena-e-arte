import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMrrHistoryQuery } from "../platformApi";
import type { MrrDataPoint } from "../platform.types";

const W      = 480;
const H      = 130;
const PAD_L  = 52;
const PAD_R  = 12;
const PAD_T  = 12;
const PAD_B  = 28;
const PW     = W - PAD_L - PAD_R;
const PH     = H - PAD_T - PAD_B;

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

function Chart({ data }: { data: MrrDataPoint[] }) {
  const n      = data.length;
  const max    = Math.max(...data.map((d) => d.mrr), 1);
  const gridY  = [0, 0.5, 1].map((t) => ({ val: max * t, y: yAt(max * t, max) }));

  const points = data.map((d, i) => ({ x: xAt(i, n), y: yAt(d.mrr, max), d }));

  const linePts = points.map((p) => `${p.x},${p.y}`).join(" ");

  const areaPath = [
    `M ${points[0].x} ${PAD_T + PH}`,
    ...points.map((p) => `L ${p.x} ${p.y}`),
    `L ${points[n - 1].x} ${PAD_T + PH}`,
    "Z",
  ].join(" ");

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      width="100%"
      aria-label="MRR trend over 12 months"
      role="img"
    >
      {/* grid lines */}
      {gridY.map(({ val, y }) => (
        <g key={val}>
          <line
            x1={PAD_L} y1={y} x2={W - PAD_R} y2={y}
            stroke="currentColor" strokeOpacity={0.12} strokeWidth={1}
          />
          <text
            x={PAD_L - 6} y={y} dominantBaseline="middle"
            textAnchor="end" fontSize={9}
            fill="currentColor" fillOpacity={0.5}
          >
            {fmtY(val)}
          </text>
        </g>
      ))}

      {/* area fill */}
      <path d={areaPath} style={{ fill: "hsl(var(--primary) / 0.08)" }} />

      {/* line */}
      <polyline
        points={linePts}
        fill="none"
        style={{ stroke: "hsl(var(--primary))" }}
        strokeWidth={2}
        strokeLinejoin="round"
        strokeLinecap="round"
      />

      {/* dots + x labels */}
      {points.map(({ x, y, d }, i) => (
        <g key={d.month}>
          <circle
            cx={x} cy={y} r={3}
            style={{ fill: "hsl(var(--primary))" }}
          />
          {/* show label every other month, or first/last */}
          {(i % 2 === 0 || i === n - 1) && (
            <text
              x={x} y={H - 4}
              textAnchor="middle" fontSize={9}
              fill="currentColor" fillOpacity={0.5}
            >
              {fmtMonth(d.month)}
            </text>
          )}
        </g>
      ))}
    </svg>
  );
}

export function MrrChart() {
  const { data, isLoading } = useGetMrrHistoryQuery();

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">MRR trend — 12 months</CardTitle>
      </CardHeader>
      <CardContent className="pt-0">
        {isLoading || !data ? (
          <Skeleton className="h-[130px] w-full" />
        ) : (
          <Chart data={data} />
        )}
      </CardContent>
    </Card>
  );
}
