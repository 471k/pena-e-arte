import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import { useEditBuffer } from "@/shared/hooks/useEditBuffer";
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
  const { currentData, isLoading } = useGetStudioHoursQuery(studio?.id ?? "", { skip: !studio?.id });
  const [upsertHours, { isLoading: saving }] = useUpsertStudioHoursMutation();

  // Filled once per studio, never on a refetch, so an edit survives one (A07a cleanup, finding 2).
  // After a save the rows already hold what was saved.
  const [buffer, setRows] = useEditBuffer<StudioHoursEntry[], DayRow[]>(
    currentData, currentData && studio ? studio.id : undefined, buildInitialRows);
  const rows = buffer ?? [];

  function updateRow(dayOfWeek: number, patch: Partial<DayRow>) {
    setRows((prev) => prev && prev.map((r) => (r.dayOfWeek === dayOfWeek ? { ...r, ...patch } : r)));
  }

  async function handleSave() {
    if (!studio || buffer === null) return;
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
              // Wraps the times onto their own line on a phone: day, switch and both times are
              // ~420px in one row, wider than a 390px screen's card (A07a cleanup, finding 3).
              <div key={row.dayOfWeek} className="flex flex-wrap items-center gap-x-3 gap-y-2" data-testid="studio-hours-row">
                <span className="w-24 shrink-0 text-sm font-medium">{DAY_LABELS[row.dayOfWeek]}</span>
                <ToggleSwitch
                  checked={row.isOpen}
                  onChange={() => updateRow(row.dayOfWeek, { isOpen: !row.isOpen })}
                  aria-label={`${DAY_LABELS[row.dayOfWeek]} open`}
                />
                <div className="flex items-center gap-2">
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
