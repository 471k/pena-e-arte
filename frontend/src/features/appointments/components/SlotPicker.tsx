import { useState } from "react";
import { AlertCircle, ChevronLeft, ChevronRight, Loader2, RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import { formatStudioTime, type StudioZone } from "@/shared/utils/formatInStudioTimezone";
import { addWallDays, formatWallDate, sameWallDate, studioWallDate, type WallDate } from "@/shared/utils/studioDay";
import type { AvailableSlotsResponse } from "../appointment.types";
import { SLOT_RANGE_DAYS, nearestStarts, parseWallDateKey, rangeStartContaining, wallDateKey } from "../slotList";

export interface SlotListQuery {
  data?:      AvailableSlotsResponse;
  isFetching: boolean;
  isError:    boolean;
  refetch:    () => unknown;
}

interface SlotPickerProps {
  id:          string;
  zone:        StudioZone;
  /** The studio's today; the range never starts before it. */
  today:       WallDate | null;
  rangeStart:  WallDate | null;
  onRangeStart: (start: WallDate) => void;
  /** The parent's RTK Query result for the current range, or null when the list can't be asked yet. */
  query:       SlotListQuery | null;
  /** Why there is no list yet (e.g. no artist chosen), shown instead of it. */
  waitingFor?: string;
  /** The instant currently in the time field, if it's a valid one. */
  selectedIso: string | null;
  onPick:      (iso: string) => void;
  /** The time the person chose and someone else booked first; the picker offers the nearest open times. */
  takenIso?:   string | null;
}

const DAY_LABEL: Intl.DateTimeFormatOptions = { weekday: "short", day: "numeric" };
const DAY_LONG: Intl.DateTimeFormatOptions = { weekday: "long", day: "numeric", month: "long" };
const TIME: Intl.DateTimeFormatOptions = { hour: "2-digit", minute: "2-digit", hourCycle: "h23" };
const DAY_TIME: Intl.DateTimeFormatOptions = { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hourCycle: "h23" };

/**
 * Open times to choose from (C03). A preview, not a reservation: choosing one only fills the time field, and the
 * booking itself decides. The time field below stays as the manual alternative, and is what to use when this
 * list is empty or can't load.
 */
export function SlotPicker({
  id, zone, today, rangeStart, onRangeStart, query, waitingFor, selectedIso, onPick, takenIso = null,
}: SlotPickerProps) {
  // Which day's times are shown. Kept per range, so moving to another week starts on its first open day.
  const [shownDay, setShownDay] = useState<{ range: string; day: string } | null>(null);

  const data = query?.data;
  const rangeKey = data ? `${data.from}/${data.to}` : "";
  const days = data?.days ?? [];
  const firstOpen = days.find((d) => d.starts.length > 0)?.date ?? days[0]?.date ?? null;
  const dayKey = shownDay?.range === rangeKey ? shownDay.day : firstOpen;
  const day = days.find((d) => d.date === dayKey) ?? null;
  const anyInRange = days.some((d) => d.starts.length > 0);
  const timeZone = zone.status === "ready" ? zone.timeZone : null;

  function jumpTo(iso: string) {
    if (!today || !timeZone) return;
    const start = rangeStartContaining(new Date(iso), today, timeZone);
    const nextRange = `${wallDateKey(start)}/${wallDateKey(addWallDays(start, SLOT_RANGE_DAYS - 1))}`;
    setShownDay({ range: nextRange, day: wallDateKey(studioWallDate(new Date(iso), timeZone)) });
    onRangeStart(start);
  }

  const alternatives = takenIso ? nearestStarts(data, takenIso) : [];

  return (
    <section aria-labelledby={`${id}-title`} className="space-y-2 rounded-md border border-border/40 p-3">
      <div className="flex items-center justify-between gap-2">
        <h3 id={`${id}-title`} className="text-xs font-medium">Open times</h3>
        {rangeStart && today && (
          <div className="flex items-center gap-1">
            <Button
              type="button" variant="ghost" size="sm" className="h-7 w-7 p-0"
              aria-label="Earlier dates"
              disabled={sameWallDate(rangeStart, today)}
              onClick={() => {
                const back = addWallDays(rangeStart, -SLOT_RANGE_DAYS);
                const earliest = Date.UTC(today.year, today.month - 1, today.day);
                onRangeStart(Date.UTC(back.year, back.month - 1, back.day) < earliest ? today : back);
              }}
            >
              <ChevronLeft className="h-4 w-4" aria-hidden="true" />
            </Button>
            <span className="text-xs text-muted-foreground tabular-nums">
              {formatWallDate(rangeStart, { day: "numeric", month: "short" })}
              {" – "}
              {formatWallDate(addWallDays(rangeStart, SLOT_RANGE_DAYS - 1), { day: "numeric", month: "short" })}
            </span>
            <Button
              type="button" variant="ghost" size="sm" className="h-7 w-7 p-0"
              aria-label="Later dates"
              onClick={() => onRangeStart(addWallDays(rangeStart, SLOT_RANGE_DAYS))}
            >
              <ChevronRight className="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        )}
      </div>

      {takenIso && (
        <div role="alert" className="space-y-2 rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs">
          <p className="flex items-start gap-1.5 font-medium text-destructive-text">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            {formatStudioTime(takenIso, DAY_TIME, zone)} was just booked by someone else. Everything else you
            entered is kept.
          </p>
          {query?.isFetching ? (
            <p className="text-muted-foreground">Finding the nearest open times…</p>
          ) : alternatives.length > 0 ? (
            <div className="space-y-1">
              <p>The nearest open times — choose one, or pick another below:</p>
              <div className="flex flex-wrap gap-1.5">
                {alternatives.map((s) => (
                  <Button key={s} type="button" variant="outline" size="sm" className="h-8 text-xs" onClick={() => onPick(s)}>
                    {formatStudioTime(s, DAY_TIME, zone)}
                  </Button>
                ))}
              </div>
            </div>
          ) : (
            <p>No other open times nearby. Try later dates, or type a time below.</p>
          )}
        </div>
      )}

      {query === null ? (
        <p className="text-xs text-muted-foreground">{waitingFor ?? "Choose the details above to see open times."}</p>
      ) : query.isError && !query.isFetching ? (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-xs">
          <p className="text-destructive-text">Couldn&apos;t load open times. You can still type a time below.</p>
          <Button type="button" variant="outline" size="sm" className="h-7 gap-1.5 text-xs" onClick={() => void query.refetch()}>
            <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
            Try again
          </Button>
        </div>
      ) : !data || (query.isFetching && data.from !== (rangeStart ? wallDateKey(rangeStart) : data.from)) ? (
        <p role="status" className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
          Finding open times…
        </p>
      ) : (
        <>
          <div role="group" aria-label="Day" className="flex gap-1 overflow-x-auto pb-1">
            {days.map((d) => {
              const wall = parseWallDateKey(d.date);
              const selected = d.date === dayKey;
              return (
                <button
                  key={d.date}
                  type="button"
                  aria-pressed={selected}
                  aria-label={`${formatWallDate(wall, DAY_LONG)}, ${d.starts.length === 0 ? "no open times" : `${d.starts.length} open time${d.starts.length === 1 ? "" : "s"}`}`}
                  onClick={() => setShownDay({ range: rangeKey, day: d.date })}
                  className={cn(
                    "flex min-h-11 min-w-12 shrink-0 flex-col items-center justify-center rounded-md border px-2 text-xs",
                    selected ? "border-primary bg-primary/10 font-medium" : "border-border/40",
                    d.starts.length === 0 && "text-muted-foreground",
                  )}
                >
                  <span>{formatWallDate(wall, DAY_LABEL)}</span>
                  <span className="text-[10px] text-muted-foreground">{d.starts.length || "–"}</span>
                </button>
              );
            })}
          </div>

          {day && day.starts.length > 0 ? (
            <div role="group" aria-label={`Open times on ${formatWallDate(parseWallDateKey(day.date), DAY_LONG)}`} className="flex flex-wrap gap-1.5">
              {day.starts.map((s) => {
                const selected = selectedIso !== null && new Date(s).getTime() === new Date(selectedIso).getTime();
                return (
                  <Button
                    key={s}
                    type="button"
                    variant={selected ? "default" : "outline"}
                    size="sm"
                    aria-pressed={selected}
                    className="h-9 min-w-16 text-xs tabular-nums"
                    onClick={() => onPick(s)}
                  >
                    {formatStudioTime(s, TIME, zone)}
                  </Button>
                );
              })}
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">
              {anyInRange ? "No open times this day." : "No open times on these dates."}
            </p>
          )}

          {!anyInRange && (
            data.nextAvailable ? (
              <Button type="button" variant="outline" size="sm" className="h-8 text-xs" onClick={() => jumpTo(data.nextAvailable!)}>
                Next open time: {formatStudioTime(data.nextAvailable, DAY_TIME, zone)}
              </Button>
            ) : (
              <p className="text-xs text-muted-foreground">
                No open times in the next 60 days. You can still type a time below to ask.
              </p>
            )
          )}
        </>
      )}
    </section>
  );
}
