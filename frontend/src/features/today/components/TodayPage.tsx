import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { CalendarDays, RotateCw, Sun } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { studioDayBounds } from "@/shared/utils/studioDay";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { useGetMyArtistQuery } from "@/features/artists/artistsApi";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { AppointmentStatus } from "@/features/appointments/appointment.types";
import { TodaySessionCard } from "./TodaySessionCard";

// The artist's daily entry point (E04a, A01): the viewer's own assigned sessions for the studio's
// current day, each with its date and time, brief, placement, references, deposit and preparation.
// The schedule (week view) is unchanged and one link away. There is no money figure here: earnings are
// DEC10 and out of scope.

const FINISHED = new Set<AppointmentStatus>([
  AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
]);
const DAY_MS = 24 * 60 * 60 * 1000;

function TodaySkeleton() {
  return (
    <div className="space-y-3" aria-label="Loading today's sessions" aria-busy="true">
      <Skeleton className="h-28 w-full rounded-lg" />
      <Skeleton className="h-28 w-full rounded-lg" />
    </div>
  );
}

export function TodayPage() {
  useDocumentMeta({ title: "Today — TattooOS", canonical: "/today" });
  const role = useAppSelector((s) => s.auth.role);
  const [now] = useState(() => new Date());

  const studioQuery = useGetMyStudioQuery();
  const timeZone = studioQuery.data?.timezone;
  // An owner reaches the week view of their own work with ?artistId (artist mode); an artist needs none.
  const { data: myArtist } = useGetMyArtistQuery(undefined, { skip: role !== Role.Owner });
  const scheduleHref = role === Role.Owner && myArtist ? `/schedule?artistId=${myArtist.id}` : "/schedule";

  // The studio's day, decided once its timezone is known. The query starts a day early so a session
  // that began last night and runs past midnight is still shown.
  const day = useMemo(() => (timeZone ? studioDayBounds(now, timeZone) : null), [now, timeZone]);
  const appointmentsQuery = useGetAppointmentsQuery(
    day
      ? { from: new Date(day.start.getTime() - DAY_MS).toISOString(), to: day.end.toISOString(), assignedToMe: true }
      : { assignedToMe: true },
    { skip: !day },
  );

  const isError = studioQuery.isError || appointmentsQuery.isError;
  const errorMessage = useSuspensionAwareError(isError, "Couldn't load today's sessions.");
  const loading = studioQuery.isLoading || (!!day && appointmentsQuery.isLoading);

  const sessions = useMemo(() => {
    if (!day || !appointmentsQuery.data) return undefined;
    return appointmentsQuery.data
      .filter((a) => new Date(a.endDate) > day.start && new Date(a.date) < day.end)
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
  }, [appointmentsQuery.data, day]);

  const dateLabel = now.toLocaleDateString(
    "en-GB", withStudioTimeZone({ weekday: "long", day: "numeric", month: "long", year: "numeric" }, timeZone));

  function retry() {
    if (studioQuery.isError) void studioQuery.refetch();
    else void appointmentsQuery.refetch();
  }

  const allFinished = !!sessions && sessions.length > 0 && sessions.every((s) => FINISHED.has(s.status));

  return (
    <main className="max-w-3xl mx-auto px-4 py-6 space-y-5">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Today</h1>
          {timeZone && <p className="text-sm text-muted-foreground">{dateLabel}</p>}
        </div>
        <Button asChild variant="outline" size="sm" className="gap-1.5 self-start sm:self-auto">
          <Link to={scheduleHref}>
            <CalendarDays aria-hidden="true" className="h-4 w-4" />
            Open schedule
          </Link>
        </Button>
      </header>

      <div aria-live="polite" className="text-xs text-muted-foreground min-h-4">
        {appointmentsQuery.isFetching && !appointmentsQuery.isLoading && sessions ? "Updating…" : null}
      </div>

      {loading ? (
        <TodaySkeleton />
      ) : isError ? (
        <div role="alert" className="flex flex-col items-center gap-3 py-12 text-center">
          <p className="text-sm text-destructive-text">{errorMessage}</p>
          <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={retry}>
            <RotateCw aria-hidden="true" className="h-3.5 w-3.5" />
            Try again
          </Button>
        </div>
      ) : sessions && sessions.length === 0 ? (
        <div className="flex flex-col items-center gap-3 py-12 text-center">
          <Sun aria-hidden="true" className="h-9 w-9 text-muted-foreground/40" />
          <p className="text-sm font-medium">No sessions today</p>
          <p className="text-xs text-muted-foreground">Your schedule shows what&apos;s coming up next.</p>
          <Button asChild size="sm"><Link to={scheduleHref}>Open your schedule</Link></Button>
        </div>
      ) : sessions ? (
        <>
          {allFinished && (
            <p className="text-sm text-muted-foreground">
              Every session today is finished or cancelled.{" "}
              <Link to={scheduleHref} className="text-link underline underline-offset-2">See what&apos;s next on your schedule</Link>.
            </p>
          )}
          <div className="space-y-3">
            {sessions.map((appt) => <TodaySessionCard key={appt.id} appt={appt} timeZone={timeZone} />)}
          </div>
        </>
      ) : null}
    </main>
  );
}
