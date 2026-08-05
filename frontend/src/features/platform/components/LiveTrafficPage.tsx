import { useState } from "react";
import "leaflet/dist/leaflet.css";
import { divIcon } from "leaflet";
import { MapContainer, Marker, Popup, TileLayer } from "react-leaflet";
import { Activity, Ghost, Palette, Shield, User } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Badge } from "@/shared/components/ui/badge";
import { LineAreaChart } from "@/shared/components/charts/LineAreaChart";
import { KpiCard, KpiSkeleton } from "./KpiCard";
import {
  useGetLiveTrafficSnapshotQuery,
  useGetTrafficHistoryQuery,
  useGetTrafficBreakdownQuery,
} from "@/features/platform/platformApi";
import { useLiveTrafficHub } from "@/shared/hooks/useLiveTrafficHub";
import type {
  LiveVisitorResponse,
  TrafficHistoryDataPoint,
  TrafficNamedCount,
  TrafficCountryCount,
} from "@/features/platform/platform.types";

const ROLE_LABELS: Record<string, string> = {
  client: "Client",
  artist: "Artist",
  owner:  "Owner",
  issuer: "Issuer",
};

function countryFlag(countryCode: string | null): string {
  if (!countryCode || countryCode.length !== 2) return "🌐";
  const codePoints = [...countryCode.toUpperCase()].map((c) => 0x1f1e6 - 65 + c.charCodeAt(0));
  return String.fromCodePoint(...codePoints);
}

function relativeTime(iso: string): string {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  return `${hours}h ago`;
}

const visitorPin = divIcon({
  className: "",
  html: `<span style="display:block;width:10px;height:10px;border-radius:9999px;background:#0f172a;border:2px solid white;box-shadow:0 0 0 1px rgba(15,23,42,0.4)"></span>`,
  iconSize: [10, 10],
  iconAnchor: [5, 5],
  popupAnchor: [0, -6],
});

const WORLD_MAP_CENTER: [number, number] = [20, 0];
const WORLD_MAP_ZOOM = 1.5;

// Only currently-active (<=60s, Redis-backed) visitors with a resolved lat/long are plotted —
// not a historical heatmap. A GeoIP miss (private/dev IP, database unavailable) just omits that
// visitor from the map; the table above/below remains the complete list regardless.
function LiveVisitorMap({ visitors }: { visitors: LiveVisitorResponse[] }) {
  const located = visitors.filter(
    (v): v is LiveVisitorResponse & { latitude: number; longitude: number } =>
      v.latitude !== null && v.longitude !== null,
  );

  return (
    <div className="relative h-[280px] rounded-md overflow-hidden border">
      <MapContainer
        center={WORLD_MAP_CENTER}
        zoom={WORLD_MAP_ZOOM}
        className="h-full w-full"
        zoomControl={false}
        scrollWheelZoom={false}
      >
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />
        {located.map((v) => (
          <Marker key={v.visitorId} position={[v.latitude, v.longitude]} icon={visitorPin}>
            <Popup>
              <div className="min-w-[140px] space-y-1 py-0.5 text-xs">
                <p className="font-semibold">
                  {v.role ? (ROLE_LABELS[v.role] ?? v.role) : "Guest"}
                </p>
                <p className="text-muted-foreground">
                  {countryFlag(v.countryCode)} {v.city ?? v.countryCode ?? "Unknown"}
                </p>
                {v.deviceType && <p className="text-muted-foreground capitalize">{v.deviceType}</p>}
              </div>
            </Popup>
          </Marker>
        ))}
      </MapContainer>
      {located.length === 0 && (
        <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-[1000] bg-background border rounded-full px-3 py-1 text-xs text-muted-foreground shadow-md">
          No located visitors right now.
        </div>
      )}
    </div>
  );
}

function VisitorRow({ visitor }: { visitor: LiveVisitorResponse }) {
  return (
    <tr className="border-b last:border-b-0">
      <td className="px-3 py-2">
        {visitor.role ? (
          <Badge variant="secondary" className="text-[10px] capitalize">{ROLE_LABELS[visitor.role] ?? visitor.role}</Badge>
        ) : (
          <Badge variant="outline" className="text-[10px]">Guest</Badge>
        )}
      </td>
      <td className="px-3 py-2 truncate max-w-[140px]">{visitor.studioName ?? "—"}</td>
      <td className="px-3 py-2 whitespace-nowrap">
        <span className="mr-1">{countryFlag(visitor.countryCode)}</span>
        {visitor.city ?? visitor.countryCode ?? "Unknown"}
      </td>
      <td className="px-3 py-2 capitalize">{visitor.deviceType ?? "—"}{visitor.browser ? ` · ${visitor.browser}` : ""}</td>
      <td className="px-3 py-2 truncate max-w-[200px] font-mono text-[11px]">{visitor.path}</td>
      <td className="px-3 py-2 whitespace-nowrap text-muted-foreground">{relativeTime(visitor.connectedAt)}</td>
    </tr>
  );
}

function LiveVisitorTable({ visitors }: { visitors: LiveVisitorResponse[] }) {
  if (visitors.length === 0) {
    return (
      <p className="text-center text-sm text-muted-foreground py-12">
        No one's on the site right now.
      </p>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b bg-muted/40 text-left text-muted-foreground">
            <th className="px-3 py-2 font-medium">Role</th>
            <th className="px-3 py-2 font-medium">Studio</th>
            <th className="px-3 py-2 font-medium">Location</th>
            <th className="px-3 py-2 font-medium">Device</th>
            <th className="px-3 py-2 font-medium">Page</th>
            <th className="px-3 py-2 font-medium">Connected</th>
          </tr>
        </thead>
        <tbody>
          {visitors.map((v) => <VisitorRow key={v.visitorId} visitor={v} />)}
        </tbody>
      </table>
    </div>
  );
}

const TREND_SERIES = [
  { key: "guestCount",  label: "Guests" },
  { key: "clientCount", label: "Clients" },
  { key: "artistCount", label: "Artists" },
  { key: "ownerCount",  label: "Owners" },
] as const;

type TrendSeriesKey = (typeof TREND_SERIES)[number]["key"];

function TrendChart({ data, series }: { data: TrafficHistoryDataPoint[]; series: TrendSeriesKey }) {
  const labelEvery = Math.ceil(data.length / 8 || 1);
  return (
    <LineAreaChart
      data={data}
      valueOf={(d) => d[series]}
      labelOf={(d, i, total) => (i % labelEvery === 0 || i === total - 1 ? d.date.slice(5) : null)}
      ariaLabel="Traffic trend"
    />
  );
}

function CountList({ items, emptyLabel }: { items: { name: string; count: number }[]; emptyLabel: string }) {
  if (items.length === 0) {
    return <p className="text-center text-xs text-muted-foreground py-6">{emptyLabel}</p>;
  }
  const max = Math.max(...items.map((i) => i.count), 1);
  return (
    <ul className="space-y-1.5">
      {items.map((item) => (
        <li key={item.name} className="flex items-center gap-2 text-xs">
          <span className="w-24 truncate shrink-0">{item.name}</span>
          <span className="flex-1 h-2 rounded-full bg-muted overflow-hidden">
            <span
              className="block h-full rounded-full bg-primary/60"
              style={{ width: `${(item.count / max) * 100}%` }}
            />
          </span>
          <span className="w-8 text-right tabular-nums text-muted-foreground">{item.count}</span>
        </li>
      ))}
    </ul>
  );
}

export function LiveTrafficPage() {
  useDocumentMeta({ title: "Live Traffic — Platform Admin", canonical: "/platform/traffic" });
  useLiveTrafficHub(true);

  const [series, setSeries] = useState<TrendSeriesKey>("guestCount");

  const { data: snapshot, isLoading: snapshotLoading, isError: snapshotError } =
    useGetLiveTrafficSnapshotQuery();
  const { data: history, isLoading: historyLoading } = useGetTrafficHistoryQuery({ days: 30 });
  const { data: breakdown, isLoading: breakdownLoading } = useGetTrafficBreakdownQuery({ days: 30 });

  const roleCounts = snapshot?.roleCounts ?? {};

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <Activity className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Live Traffic</span>
        {snapshot && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {snapshot.totalActive} active now
          </span>
        )}
      </header>

      <main className="max-w-4xl mx-auto px-4 py-4 space-y-6">
        {snapshotError && (
          <p className="text-center text-sm text-destructive py-4" role="alert">
            Failed to load live traffic.
          </p>
        )}

        <section className="grid grid-cols-2 sm:grid-cols-5 gap-3">
          {snapshotLoading || !snapshot ? (
            <>{Array.from({ length: 5 }, (_, i) => <KpiSkeleton key={i} />)}</>
          ) : (
            <>
              <KpiCard label="Active now" value={snapshot.totalActive} icon={<Activity className="h-5 w-5" />} accent="info" />
              <KpiCard label="Guests" value={snapshot.guestCount} icon={<Ghost className="h-5 w-5" />} />
              <KpiCard label="Clients" value={roleCounts.client ?? 0} icon={<User className="h-5 w-5" />} />
              <KpiCard label="Artists" value={roleCounts.artist ?? 0} icon={<Palette className="h-5 w-5" />} />
              <KpiCard label="Owners" value={roleCounts.owner ?? 0} icon={<Shield className="h-5 w-5" />} />
            </>
          )}
        </section>

        <section>
          <p className="text-sm font-medium mb-2">Live visitor map</p>
          {snapshotLoading || !snapshot ? (
            <div className="h-[280px] rounded-md bg-muted animate-pulse" />
          ) : (
            <LiveVisitorMap visitors={snapshot.visitors} />
          )}
        </section>

        <section>
          <p className="text-sm font-medium mb-2">Who's here right now</p>
          {snapshotLoading || !snapshot ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <div key={i} className="h-8 rounded bg-muted animate-pulse" />)}
            </div>
          ) : (
            <LiveVisitorTable visitors={snapshot.visitors} />
          )}
        </section>

        <Card>
          <CardHeader className="pb-2">
            <div className="flex items-center justify-between gap-2">
              <CardTitle className="text-sm">Traffic trend (30 days)</CardTitle>
              <div className="flex items-center gap-1 flex-wrap justify-end">
                {TREND_SERIES.map(({ key, label }) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setSeries(key)}
                    className={`text-[11px] px-2 py-0.5 rounded transition-colors ${
                      series === key
                        ? "bg-primary text-primary-foreground"
                        : "text-muted-foreground hover:text-foreground hover:bg-muted"
                    }`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </CardHeader>
          <CardContent className="pt-0">
            {historyLoading || !history ? (
              <div className="h-[130px] rounded bg-muted animate-pulse" />
            ) : history.dataPoints.length === 0 ? (
              <p className="h-[130px] flex items-center justify-center text-xs text-muted-foreground">
                No traffic data yet.
              </p>
            ) : (
              <TrendChart data={history.dataPoints} series={series} />
            )}
          </CardContent>
        </Card>

        <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Top countries</CardTitle></CardHeader>
            <CardContent className="pt-0">
              {breakdownLoading || !breakdown ? (
                <div className="h-24 rounded bg-muted animate-pulse" />
              ) : (
                <CountList
                  items={breakdown.topCountries.map((c: TrafficCountryCount) => ({
                    name: `${countryFlag(c.countryCode)} ${c.countryCode ?? "Unknown"}`,
                    count: c.count,
                  }))}
                  emptyLabel="No geography data yet."
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Device / browser</CardTitle></CardHeader>
            <CardContent className="pt-0">
              {breakdownLoading || !breakdown ? (
                <div className="h-24 rounded bg-muted animate-pulse" />
              ) : (
                <CountList
                  items={[...breakdown.deviceBreakdown, ...breakdown.browserBreakdown].map(
                    (b: TrafficNamedCount) => ({ name: b.name, count: b.count })
                  )}
                  emptyLabel="No device data yet."
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Top pages</CardTitle></CardHeader>
            <CardContent className="pt-0">
              {breakdownLoading || !breakdown ? (
                <div className="h-24 rounded bg-muted animate-pulse" />
              ) : (
                <CountList
                  items={breakdown.topPages.map((p: TrafficNamedCount) => ({ name: p.name, count: p.count }))}
                  emptyLabel="No page data yet."
                />
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Top networks</CardTitle></CardHeader>
            <CardContent className="pt-0">
              {breakdownLoading || !breakdown ? (
                <div className="h-24 rounded bg-muted animate-pulse" />
              ) : (
                <CountList
                  items={breakdown.topNetworks.map((n: TrafficNamedCount) => ({ name: n.name, count: n.count }))}
                  emptyLabel="No network data yet."
                />
              )}
            </CardContent>
          </Card>
        </div>
      </main>
    </div>
  );
}
