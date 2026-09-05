import { useState } from "react";
import { Loader2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import {
  useGetArtistScheduleQuery,
  useUpsertArtistScheduleMutation,
  useAddArtistTimeOffMutation,
  useDeleteArtistTimeOffMutation,
  type ArtistScheduleEntry,
} from "../artistsApi";

const DAY_LABELS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const DEFAULT_START = "09:00:00";
const DEFAULT_END   = "18:00:00";

interface DayRow {
  dayOfWeek:   number;
  isAvailable: boolean;
  startTime:   string; // "HH:mm"
  endTime:     string; // "HH:mm"
}

function toHm(value: string): string {
  return value.slice(0, 5);
}

function buildInitialRows(entries: ArtistScheduleEntry[]): DayRow[] {
  return DAY_LABELS.map((_, dayOfWeek) => {
    const existing = entries.find((e) => e.dayOfWeek === dayOfWeek);
    return existing
      ? {
          dayOfWeek,
          isAvailable: existing.isAvailable,
          startTime:   toHm(existing.startTime),
          endTime:     toHm(existing.endTime),
        }
      : {
          dayOfWeek,
          isAvailable: false,
          startTime:   toHm(DEFAULT_START),
          endTime:     toHm(DEFAULT_END),
        };
  });
}

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

interface ArtistScheduleEditorProps {
  artistId: string;
  canEdit:  boolean;
}

export function ArtistScheduleEditor({ artistId, canEdit }: ArtistScheduleEditorProps) {
  const { data, isLoading } = useGetArtistScheduleQuery(artistId);
  const [upsertSchedule, { isLoading: saving }] = useUpsertArtistScheduleMutation();
  const [addTimeOff, { isLoading: addingTimeOff }] = useAddArtistTimeOffMutation();
  const [deleteTimeOff, { isLoading: deletingTimeOff }] = useDeleteArtistTimeOffMutation();

  const [rows, setRows] = useState<DayRow[]>([]);
  // Seeded with a sentinel distinct from `data`, not with `data` itself —
  // otherwise data already present on the very first render (e.g. an RTK Query
  // cache hit) would look "already synced" and never populate `rows` below.
  const [syncedData, setSyncedData] = useState<typeof data | undefined>(undefined);

  // Sync rows from freshly-fetched data. Adjusting state during render (rather
  // than in an effect) avoids an extra post-effect render pass — React discards
  // this in-progress render and immediately restarts with the new state — see
  // https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes.
  if (data !== syncedData) {
    setSyncedData(data);
    if (data) setRows(buildInitialRows(data.schedule));
  }

  const [timeOffFormOpen, setTimeOffFormOpen] = useState(false);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate]     = useState("");
  const [reason, setReason]       = useState("");
  const [timeOffError, setTimeOffError] = useState<string | null>(null);

  function updateRow(dayOfWeek: number, patch: Partial<DayRow>) {
    setRows((prev) => prev.map((r) => (r.dayOfWeek === dayOfWeek ? { ...r, ...patch } : r)));
  }

  async function handleSaveSchedule() {
    const entries = rows.map((r) => ({
      dayOfWeek:   r.dayOfWeek,
      startTime:   `${r.startTime}:00`,
      endTime:     `${r.endTime}:00`,
      isAvailable: r.isAvailable,
    }));
    try {
      await upsertSchedule({ artistId, entries }).unwrap();
      toast.success("Working hours saved.");
    } catch {
      toast.error("Failed to save working hours.");
    }
  }

  function resetTimeOffForm() {
    setStartDate("");
    setEndDate("");
    setReason("");
    setTimeOffError(null);
    setTimeOffFormOpen(false);
  }

  async function handleAddTimeOff() {
    if (!startDate || !endDate || !reason.trim()) {
      setTimeOffError("All fields are required.");
      return;
    }
    if (endDate < startDate) {
      setTimeOffError("End date must be on or after start date.");
      return;
    }
    try {
      await addTimeOff({ artistId, body: { startDate, endDate, reason: reason.trim() } }).unwrap();
      toast.success("Time off added.");
      resetTimeOffForm();
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "data" in err && err.data &&
        typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Failed to add time off.";
      setTimeOffError(message);
    }
  }

  async function handleDeleteTimeOff(timeOffId: string) {
    try {
      await deleteTimeOff({ artistId, timeOffId }).unwrap();
      toast.success("Time off removed.");
    } catch {
      toast.error("Failed to remove time off.");
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-2">
        {[1, 2, 3].map((i) => <Skeleton key={i} className="h-10 w-full" />)}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Working hours</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {rows.map((row) => (
            <div key={row.dayOfWeek} className="flex items-center gap-3">
              <span className="w-24 shrink-0 text-sm font-medium">{DAY_LABELS[row.dayOfWeek]}</span>
              <ToggleSwitch
                checked={row.isAvailable}
                onChange={() => canEdit && updateRow(row.dayOfWeek, { isAvailable: !row.isAvailable })}
                disabled={!canEdit}
                aria-label={`${DAY_LABELS[row.dayOfWeek]} available`}
              />
              <Input
                type="time"
                value={row.startTime}
                onChange={(e) => updateRow(row.dayOfWeek, { startTime: e.target.value })}
                disabled={!canEdit || !row.isAvailable}
                aria-label={`${DAY_LABELS[row.dayOfWeek]} start time`}
                className="w-28"
              />
              <span className="text-muted-foreground text-sm">–</span>
              <Input
                type="time"
                value={row.endTime}
                onChange={(e) => updateRow(row.dayOfWeek, { endTime: e.target.value })}
                disabled={!canEdit || !row.isAvailable}
                aria-label={`${DAY_LABELS[row.dayOfWeek]} end time`}
                className="w-28"
              />
            </div>
          ))}

          {canEdit && (
            <Button onClick={handleSaveSchedule} disabled={saving} className="gap-2 mt-2">
              {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Save working hours
            </Button>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Time off</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {data && data.timeOff.length > 0 ? (
            <ul className="space-y-2">
              {data.timeOff.map((t) => (
                <li key={t.id} className="flex items-center justify-between gap-3 rounded-md border px-3 py-2">
                  <div className="min-w-0">
                    <p className="text-sm font-medium truncate">{t.reason}</p>
                    <p className="text-xs text-muted-foreground">
                      {formatDate(t.startDate)} – {formatDate(t.endDate)}
                    </p>
                  </div>
                  {canEdit && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7 shrink-0"
                      onClick={() => handleDeleteTimeOff(t.id)}
                      disabled={deletingTimeOff}
                      aria-label={`Remove time off: ${t.reason}`}
                    >
                      <Trash2 className="h-3.5 w-3.5 text-destructive" />
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground italic">No upcoming time off.</p>
          )}

          {canEdit && (
            timeOffFormOpen ? (
              <div className="space-y-3 rounded-md border p-3">
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <Label htmlFor="timeoff-start">Start date</Label>
                    <Input
                      id="timeoff-start"
                      type="date"
                      value={startDate}
                      onChange={(e) => setStartDate(e.target.value)}
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="timeoff-end">End date</Label>
                    <Input
                      id="timeoff-end"
                      type="date"
                      value={endDate}
                      onChange={(e) => setEndDate(e.target.value)}
                    />
                  </div>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="timeoff-reason">Reason</Label>
                  <Input
                    id="timeoff-reason"
                    placeholder="e.g. Holiday"
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    maxLength={500}
                  />
                </div>
                {timeOffError && <p className="text-xs text-destructive-text">{timeOffError}</p>}
                <div className="flex items-center gap-2">
                  <Button size="sm" onClick={handleAddTimeOff} disabled={addingTimeOff} className="gap-2">
                    {addingTimeOff && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                    Add time off
                  </Button>
                  <Button variant="ghost" size="sm" onClick={resetTimeOffForm} disabled={addingTimeOff} className="gap-1">
                    <X className="h-3.5 w-3.5" />
                    Cancel
                  </Button>
                </div>
              </div>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setTimeOffFormOpen(true)}>
                Add time off
              </Button>
            )
          )}
        </CardContent>
      </Card>
    </div>
  );
}
