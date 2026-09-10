import { useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import {
  useGetMyStudioQuery,
  useGetStudioHoursQuery,
  useUpsertStudioHoursMutation,
  type StudioHoursEntry,
} from "../studiosApi";

const DAY_LABELS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const DEFAULT_START = "09:00:00";
const DEFAULT_END   = "18:00:00";

interface DayRow {
  dayOfWeek: number;
  isOpen:    boolean;
  startTime: string; // "HH:mm"
  endTime:   string; // "HH:mm"
}

function toHm(value: string): string {
  return value.slice(0, 5);
}

function buildInitialRows(entries: StudioHoursEntry[]): DayRow[] {
  return DAY_LABELS.map((_, dayOfWeek) => {
    const existing = entries.find((e) => e.dayOfWeek === dayOfWeek);
    return existing
      ? {
          dayOfWeek,
          isOpen:    existing.isOpen,
          startTime: toHm(existing.startTime),
          endTime:   toHm(existing.endTime),
        }
      : {
          dayOfWeek,
          isOpen:    false,
          startTime: toHm(DEFAULT_START),
          endTime:   toHm(DEFAULT_END),
        };
  });
}

export function StudioHoursCard() {
  const { data: studio } = useGetMyStudioQuery();
  const { data, isLoading } = useGetStudioHoursQuery(studio?.id ?? "", { skip: !studio?.id });
  const [upsertHours, { isLoading: saving }] = useUpsertStudioHoursMutation();

  const [rows, setRows] = useState<DayRow[]>([]);
  // Seeded with a sentinel distinct from `data`, not with `data` itself — otherwise data
  // already present on the very first render (e.g. an RTK Query cache hit) would look
  // "already synced" and never populate `rows` below.
  const [syncedData, setSyncedData] = useState<typeof data | undefined>(undefined);

  // Sync rows from freshly-fetched data. Adjusting state during render (rather than in an
  // effect) avoids an extra post-effect render pass.
  if (data !== syncedData) {
    setSyncedData(data);
    if (data) setRows(buildInitialRows(data));
  }

  function updateRow(dayOfWeek: number, patch: Partial<DayRow>) {
    setRows((prev) => prev.map((r) => (r.dayOfWeek === dayOfWeek ? { ...r, ...patch } : r)));
  }

  async function handleSave() {
    if (!studio) return;
    const entries: StudioHoursEntry[] = rows.map((r) => ({
      dayOfWeek: r.dayOfWeek,
      startTime: `${r.startTime}:00`,
      endTime:   `${r.endTime}:00`,
      isOpen:    r.isOpen,
    }));
    try {
      await upsertHours({ id: studio.id, body: { entries } }).unwrap();
      toast.success("Studio hours saved.");
    } catch {
      toast.error("Failed to save studio hours.");
    }
  }

  if (!studio) return null;

  return (
    <Card data-tour="owner-studio-hours-card">
      <CardHeader>
        <CardTitle className="text-base">Studio hours</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Clients can only book appointments within these hours, regardless of any individual
          artist's own working hours. A day with no hours set is treated as closed.
        </p>

        {isLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-10 w-full" />)}
          </div>
        ) : (
          <>
            {rows.map((row) => (
              <div key={row.dayOfWeek} className="flex items-center gap-3">
                <span className="w-24 shrink-0 text-sm font-medium">{DAY_LABELS[row.dayOfWeek]}</span>
                <ToggleSwitch
                  checked={row.isOpen}
                  onChange={() => updateRow(row.dayOfWeek, { isOpen: !row.isOpen })}
                  aria-label={`${DAY_LABELS[row.dayOfWeek]} open`}
                />
                <Input
                  type="time"
                  value={row.startTime}
                  onChange={(e) => updateRow(row.dayOfWeek, { startTime: e.target.value })}
                  disabled={!row.isOpen}
                  aria-label={`${DAY_LABELS[row.dayOfWeek]} opening time`}
                  className="w-28"
                />
                <span className="text-muted-foreground text-sm">–</span>
                <Input
                  type="time"
                  value={row.endTime}
                  onChange={(e) => updateRow(row.dayOfWeek, { endTime: e.target.value })}
                  disabled={!row.isOpen}
                  aria-label={`${DAY_LABELS[row.dayOfWeek]} closing time`}
                  className="w-28"
                />
              </div>
            ))}

            <Button onClick={handleSave} disabled={saving} className="gap-2 mt-2">
              {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Save studio hours
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
