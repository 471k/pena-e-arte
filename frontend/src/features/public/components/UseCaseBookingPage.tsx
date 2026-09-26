import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";

// Marketing page /use/booking. Copy describes shipped behaviour only (guest checkout,
// artist schedules/time off/closures, waitlist, client self-service cancel/reschedule).
export function UseCaseBookingPage() {
  return (
    <PublicContentLayout
      title="Online booking for tattoo studios — TattooOS"
      description="Let clients book the artist and time they want, without an account. Availability is checked before a booking is confirmed."
      canonicalPath="/use/booking"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Online booking for tattoo studios</h1>

      <p className="mt-4 text-muted-foreground">
        Clients choose an artist, pick a time that is actually free and send their idea along
        with reference images. They can book as a guest, without creating an account first,
        and returning clients book from their own profile.
      </p>
      <p className="mt-4 text-muted-foreground">
        Every artist keeps their own schedule, time off and studio closures. Availability is
        checked before a booking is confirmed, so the same slot cannot be booked twice, and a
        waitlist picks up the cancellations.
      </p>
      <p className="mt-4 text-muted-foreground">
        Clients can cancel or reschedule from their own account, within the window your
        studio sets, without a round of messages.
      </p>

      <MarketingCtaRow />
    </PublicContentLayout>
  );
}
