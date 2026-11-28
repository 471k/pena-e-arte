import { Link } from "react-router-dom";
import { CalendarDays, CalendarPlus } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { ResourceEmptyState } from "@/shared/components/ResourceEmptyState";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";
import { BookingWidget } from "@/features/booking/components/BookingWidget";
import { ReferAFriendCard } from "@/features/client-referrals/components/ReferAFriendCard";
import { useGetMyAppointmentsQuery } from "../appointmentsApi";
import { MyBookingsSection } from "./MyBookingsSection";

/**
 * The client's home (C01). Until slice 6d a client landed on /book, the booking FORM, while an artist landed on
 * /today and an owner on /dashboard: the client was the only role whose home was a task rather than a view of
 * their own things. Their bookings sat underneath the form.
 *
 * Booking stays one obvious action away (the button in the header, and the menu's "Book Appointment"). A client
 * with no bookings yet gets an empty state whose primary action IS booking, so a first visit doesn't look broken.
 */
export function MyBookingsPage() {
  useDocumentMeta({ title: "My bookings — TattooOS", canonical: "/my-appointments" });
  const tenantId = useAppSelector((s) => s.auth.tenantId);
  // Same cache entry MyBookingsSection reads, so this costs no extra request.
  const { data: appointments, isLoading, isError } = useGetMyAppointmentsQuery(undefined, { skip: !tenantId });

  if (!tenantId) {
    return (
      <main className="max-w-md mx-auto px-4 py-12">
        <Card>
          <CardContent className="p-0">
            <ResourceEmptyState
              icon={<CalendarDays className="h-9 w-9" />}
              heading="You haven't chosen a studio yet"
              body="Find a studio to book your first appointment. Your bookings will show here."
              action={<Button asChild size="sm"><Link to="/discover">Browse studios</Link></Button>}
            />
          </CardContent>
        </Card>
      </main>
    );
  }

  const hasNone = !isLoading && !isError && (appointments?.length ?? 0) === 0;

  return (
    <BookingWidget>
      <main className="max-w-md mx-auto px-4 py-8 space-y-6">
        {/* The page's heading is the bookings card's own "My bookings" (one h1, not two). */}
        {!hasNone && (
          <div className="flex justify-end">
            <Button asChild size="sm" className="gap-1.5">
              <Link to="/book">
                <CalendarPlus className="h-4 w-4" aria-hidden="true" />
                Book an appointment
              </Link>
            </Button>
          </div>
        )}

        {hasNone ? (
          <Card>
            <CardContent className="p-0">
              <h1 className="px-6 pt-6 text-base font-semibold leading-none tracking-tight">My bookings</h1>
              <ResourceEmptyState
                icon={<CalendarPlus className="h-9 w-9" />}
                heading="No bookings yet"
                body="Book your first appointment and it will show here, with what to do before it and any deposit to pay."
                action={<Button asChild size="sm" wrapLabel><Link to="/book">Book an appointment</Link></Button>}
              />
            </CardContent>
          </Card>
        ) : (
          // Loading and error are MyBookingsSection's own states.
          <MyBookingsSection />
        )}

        <ReferAFriendCard />
      </main>
    </BookingWidget>
  );
}
