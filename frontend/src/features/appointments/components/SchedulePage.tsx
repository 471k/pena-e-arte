import { useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { CalendarDays, ChevronLeft, ChevronRight, MessageSquarePlus, PenLine } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetAppointmentsQuery } from "../appointmentsApi";
import { AppointmentCard } from "./AppointmentCard";
import { ReminderDialog } from "@/features/reminders/components/ReminderDialog";

function SchedulePageSkeleton() {
  return (
    <main className="max-w-3xl mx-auto px-4 py-6 space-y-8" aria-label="Loading schedule">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="space-y-2">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-14 w-full rounded-lg" />
          {i === 0 && <Skeleton className="h-14 w-full rounded-lg" />}
        </div>
      ))}
    </main>
  );
}

function getWeekStart(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  d.setDate(d.getDate() + diff);
  d.setHours(0, 0, 0, 0);
  return d;
}

function addDays(date: Date, days: number): Date {
  const d = new Date(date);
  d.setDate(d.getDate() + days);
  return d;
}

function isSameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth()    === b.getMonth()    &&
    a.getDate()     === b.getDate()
  );
}

const DAY_NAMES = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

export function SchedulePage() {
  useDocumentMeta({ title: "Schedule — TattooOS", canonical: "/schedule" });

  const [weekStart, setWeekStart] = useState(() => getWeekStart(new Date()));
  const [quickReminderOpen, setQuickReminderOpen] = useState(false);
  // Exact-role (not usePermission's rank-based "Artist and above") check: raw-contact
  // reminders require an ArtistId the backend can only infer for the artist themselves —
  // owner/issuer have no artist context on this page and no artist-picker exists yet, so
  // showing this button to them would open a dialog that always 422s on submit.
  const role = useAppSelector((s) => s.auth.role);
  const canQuickRemind = role === Role.Artist;

  const weekEnd = addDays(weekStart, 7);

  const { data: appointments, isLoading, isError } = useGetAppointmentsQuery({
    from: weekStart.toISOString(),
    to:   weekEnd.toISOString(),
  });

  const errorMessage = useSuspensionAwareError(isError, "Failed to load appointments. Please try again.");

  const weekLabel =
    weekStart.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" }) +
    " – " +
    addDays(weekStart, 6).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });

  const today = new Date();
  const isCurrentWeek = weekStart.getTime() === getWeekStart(today).getTime();
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i));

  const weekHasAppointments = days.some((day) =>
    (appointments ?? []).some((a) => isSameDay(new Date(a.date), day))
  );

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Schedule</span>
        </div>

        <div className="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setWeekStart((w) => addDays(w, -7))}
            aria-label="Previous week"
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>

          <div className="flex items-center gap-1.5 text-sm px-2 min-w-[230px] justify-center">
            <CalendarDays className="h-4 w-4 text-muted-foreground shrink-0" />
            <span>{weekLabel}</span>
          </div>

          <Button
            variant="ghost"
            size="icon"
            onClick={() => setWeekStart((w) => addDays(w, 7))}
            aria-label="Next week"
          >
            <ChevronRight className="h-4 w-4" />
          </Button>

          <Button
            variant="outline"
            size="sm"
            className="ml-2 text-xs"
            disabled={isCurrentWeek}
            onClick={() => setWeekStart(getWeekStart(new Date()))}
          >
            Today
          </Button>

          {canQuickRemind && (
            <Button
              variant="ghost"
              size="icon"
              className="ml-1"
              onClick={() => setQuickReminderOpen(true)}
              aria-label="Quick reminder"
            >
              <MessageSquarePlus className="h-4 w-4" />
            </Button>
          )}
        </div>
      </header>

      {isLoading ? (
        <SchedulePageSkeleton />
      ) : (
        <main className="max-w-3xl mx-auto px-4 py-6 space-y-8">
          {errorMessage && (
            <p className="text-center text-sm text-destructive py-16" role="alert">
              {errorMessage}
            </p>
          )}

          {!isError && !weekHasAppointments && appointments !== undefined && (
            <div className="flex flex-col items-center gap-3 py-16 text-center">
              <CalendarDays className="h-9 w-9 text-muted-foreground/40" />
              <p className="text-sm font-medium">No appointments this week</p>
              <p className="text-xs text-muted-foreground">
                Use the arrows to navigate to a different week.
              </p>
            </div>
          )}

          {!isError && weekHasAppointments && days.map((day, i) => {
            const isToday = isSameDay(day, today);
            const dayAppointments = (appointments ?? [])
              .filter((a) => isSameDay(new Date(a.date), day))
              .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());

            return (
              <section key={day.toISOString()}>
                <div className="flex items-center gap-2 mb-3">
                  <span className={`text-sm font-semibold ${isToday ? "text-primary" : "text-foreground"}`}>
                    {DAY_NAMES[i]}
                  </span>
                  <span className={`text-sm ${isToday ? "text-primary" : "text-muted-foreground"}`}>
                    {day.toLocaleDateString("en-GB", { day: "numeric", month: "short" })}
                  </span>
                  {isToday && (
                    <span className="text-xs bg-primary text-primary-foreground rounded-full px-2 py-0.5 font-medium">
                      Today
                    </span>
                  )}
                  {dayAppointments.length > 0 && (
                    <span className="ml-auto text-xs text-muted-foreground">
                      {dayAppointments.length} appointment{dayAppointments.length !== 1 ? "s" : ""}
                    </span>
                  )}
                </div>

                {dayAppointments.length === 0 ? (
                  <p className="text-xs text-muted-foreground pl-1">No appointments</p>
                ) : (
                  <div className="space-y-2">
                    {dayAppointments.map((appt) => (
                      <AppointmentCard key={appt.id} appointment={appt} />
                    ))}
                  </div>
                )}
              </section>
            );
          })}
        </main>
      )}

      <ReminderDialog open={quickReminderOpen} onOpenChange={setQuickReminderOpen} />
    </div>
  );
}
