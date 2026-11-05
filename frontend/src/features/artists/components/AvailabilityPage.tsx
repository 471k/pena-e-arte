import type { ReactNode } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ArrowLeft, CalendarClock } from "lucide-react";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetMyArtistQuery } from "../artistsApi";
import { ArtistScheduleEditor } from "./ArtistScheduleEditor";

// E04c (A06): the viewer's own working hours and time off, one step from the schedule rather than
// buried in the artist profile's Availability tab. It is the same editor (ArtistScheduleEditor) for the
// same artist row, not a second copy of SchedulePage. Reached from Schedule's "Availability" link, shown
// to an artist and to an owner in their own artist view; the owner's ?artistId= is kept on the way back.

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status: unknown }).status;
    return typeof status === "number" ? status : undefined;
  }
  return undefined;
}

export function AvailabilityPage() {
  useDocumentMeta({ title: "Availability — TattooOS", canonical: "/schedule/availability" });
  const [searchParams] = useSearchParams();
  const artistParam = searchParams.get("artistId");
  const backTo = artistParam ? `/schedule?artistId=${encodeURIComponent(artistParam)}` : "/schedule";
  const { data: me, isLoading, isError, error } = useGetMyArtistQuery();

  let body: ReactNode;
  if (isLoading) {
    body = (
      <div className="space-y-2" aria-label="Loading availability">
        {[1, 2, 3].map((i) => <Skeleton key={i} className="h-10 w-full" />)}
      </div>
    );
  } else if (isError && errorStatus(error) === 404) {
    body = (
      <p className="text-sm text-muted-foreground">
        You don&apos;t have an artist profile in this studio, so there are no working hours or time off to set
        here. An owner sets an artist&apos;s hours from Artists, on that artist&apos;s Availability tab.
      </p>
    );
  } else if (isError || !me) {
    body = (
      <p role="alert" className="text-sm text-destructive-text">
        Couldn&apos;t load your availability. Please try again.
      </p>
    );
  } else {
    body = <ArtistScheduleEditor artistId={me.id} canEdit />;
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex flex-wrap items-center justify-between gap-2 px-4 sm:px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Link
          to={backTo}
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm font-medium hover:bg-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Schedule
        </Link>
        <div className="flex items-center gap-2">
          <CalendarClock className="h-5 w-5" aria-hidden="true" />
          <h1 className="font-semibold tracking-tight">Working hours &amp; time off</h1>
        </div>
      </header>
      <main className="max-w-3xl mx-auto px-4 py-6">{body}</main>
    </div>
  );
}
