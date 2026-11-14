import { useMemo, type ReactNode } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { Bell, CalendarClock } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { PublicPageHeader } from "@/features/public/components/PublicPageHeader";
import { useAppSelector } from "@/app/hooks";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { useGetWaitlistClaimQuery } from "../waitlistApi";
import { WaitlistClaimState, type WaitlistClaimResponse } from "../waitlist.types";
import { readClaimTokenFromHash, type WaitlistClaimHandoff } from "../waitlistClaim";

const SLOT_FORMAT: Intl.DateTimeFormatOptions = {
  weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit",
};

function formatInStudio(iso: string, timezone: string): string {
  return new Date(iso).toLocaleString("en-GB", withStudioTimeZone(SLOT_FORMAT, timezone));
}

function isNotFound(error: unknown): boolean {
  return typeof error === "object" && error !== null && "status" in error
    && (error as FetchBaseQueryError).status === 404;
}

/**
 * C09: where the "a slot opened up" email lands, signed in or not. The token is in the URL fragment
 * (`/waitlist/claim#t=…`), which browsers never send to a server; this page posts it in a request body.
 * Viewing never uses up the offer. Every outcome has its own wording, and none of them is "Page not found".
 */
export function WaitlistClaimPage() {
  useDocumentMeta({ title: "Your waitlist offer — TattooOS", canonical: "/waitlist/claim" });
  const role = useAppSelector((s) => s.auth.role);
  const { hash } = useLocation();
  const token = useMemo(() => readClaimTokenFromHash(hash), [hash]);

  const { data, isLoading, isError, error, refetch } = useGetWaitlistClaimQuery(token ?? "", { skip: !token });

  let body: ReactNode;
  if (!token) {
    body = <Message title="This link is incomplete">
      Open the link straight from your waitlist email. If you copied it, make sure you copied all of it.
    </Message>;
  } else if (isLoading) {
    body = <div className="space-y-3" aria-label="Loading your waitlist offer">
      <Skeleton className="h-6 w-2/3" />
      <Skeleton className="h-20 w-full" />
    </div>;
  } else if (isError && isNotFound(error)) {
    body = <Message title="This link isn't valid">
      It may be from an older email, or it was copied only in part. If you're still on the waitlist, the
      newest email the studio sent you has the link that works.
    </Message>;
  } else if (isError || !data) {
    body = <Message title="We couldn't load your offer">
      <button type="button" className="underline" onClick={() => refetch()}>Try again</button>
    </Message>;
  } else {
    body = <Offer claim={data} token={token} signedIn={role !== null} />;
  }

  const content = (
    <div className="flex-1 flex items-start justify-center px-4 py-12">
      <div className="w-full max-w-md">
        <Card>
          <CardHeader className="pb-2">
            <div className="flex items-center gap-2">
              <Bell className="h-4 w-4 text-info-text" aria-hidden="true" />
              <h1 className="text-base font-semibold tracking-tight">Your waitlist offer</h1>
            </div>
          </CardHeader>
          <CardContent>{body}</CardContent>
        </Card>
      </div>
    </div>
  );

  // Signed-in visitors get the app shell from AppLayout; signed-out ones get the public header.
  return role ? content : (
    <div className="min-h-screen bg-background flex flex-col">
      <PublicPageHeader />
      {content}
    </div>
  );
}

function Message({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="space-y-2" role="status">
      <p className="text-sm font-medium">{title}</p>
      <p className="text-sm text-muted-foreground">{children}</p>
    </div>
  );
}

function Offer({ claim, token, signedIn }: { claim: WaitlistClaimResponse; token: string; signedIn: boolean }) {
  const navigate = useNavigate();
  const slot = formatInStudio(claim.slotStart, claim.studioTimezone);
  const windowEnds = formatInStudio(claim.windowEndsAt, claim.studioTimezone);
  const withWhom = claim.artistName ? ` with ${claim.artistName}` : "";

  function book(slotStart: string | null) {
    const handoff: WaitlistClaimHandoff = {
      token, artistId: claim.artistId, slotStart, durationMinutes: claim.durationMinutes,
    };
    navigate(`/book?studio=${encodeURIComponent(claim.studioSlug)}`, { state: { waitlistClaim: handoff } });
  }

  const slotLine = (
    <p className="flex items-center gap-2 text-sm">
      <CalendarClock className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
      <span><span className="font-medium">{slot}</span>{withWhom} at {claim.studioName}</span>
    </p>
  );

  switch (claim.state) {
    case WaitlistClaimState.Claimable:
      return (
        <div className="space-y-4">
          <p className="text-sm">A slot you were waiting for is free.</p>
          {slotLine}
          <p className="text-xs text-muted-foreground">
            The slot isn&apos;t held for you: whoever books it first gets it. You&apos;re the person being offered it
            until {windowEnds} (studio time); after that the offer moves to the next person in line.
          </p>
          <Button className="w-full" onClick={() => book(claim.slotStart)}>Book this slot</Button>
        </div>
      );
    case WaitlistClaimState.SlotTaken:
      return (
        <div className="space-y-4">
          <p className="text-sm font-medium">Someone booked this slot before you</p>
          {slotLine}
          <p className="text-xs text-muted-foreground">
            Waitlist slots aren&apos;t held: they go to whoever books first. You can still book another time with
            {claim.artistName ? ` ${claim.artistName}` : " the studio"}.
          </p>
          <Button variant="outline" className="w-full" onClick={() => book(null)}>Book another time</Button>
        </div>
      );
    case WaitlistClaimState.Booked:
      return (
        <div className="space-y-3">
          <p className="text-sm font-medium">You&apos;ve booked from this offer</p>
          <p className="text-sm text-muted-foreground">
            {signedIn ? "You'll find it with your bookings." : "The booking details are in your email."}
          </p>
          {signedIn && <Button asChild variant="outline" className="w-full"><Link to="/book">See your bookings</Link></Button>}
        </div>
      );
    case WaitlistClaimState.WindowEnded:
      return (
        <div className="space-y-3">
          <p className="text-sm font-medium">This offer has ended</p>
          {slotLine}
          <p className="text-sm text-muted-foreground">
            Each waitlist offer is open for 24 hours, and this one has moved on to the next person. You can join the
            waitlist again from the studio&apos;s page.
          </p>
          <Button asChild variant="outline" className="w-full">
            <Link to={`/s/${encodeURIComponent(claim.studioSlug)}`}>Go to {claim.studioName}</Link>
          </Button>
        </div>
      );
    case WaitlistClaimState.Cancelled:
      return (
        <div className="space-y-3">
          <p className="text-sm font-medium">You left this waitlist</p>
          <p className="text-sm text-muted-foreground">
            Because you removed yourself from the waitlist, this offer is no longer open.
          </p>
        </div>
      );
  }
}
