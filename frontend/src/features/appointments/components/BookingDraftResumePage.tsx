import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from "react-router-dom";
import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { History } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { PublicPageHeader } from "@/features/public/components/PublicPageHeader";
import { useGetPublicStudioQuery } from "@/features/public/publicApi";
import { useEnsureActiveStudio } from "@/features/auth/useEnsureActiveStudio";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useResumeBookingDraftMutation } from "../appointmentsApi";
import { readResumeTokenFromHash, type ResumeBookingDraftResponse } from "../bookingDraft";

/** Where the token waits while the person signs in. Same tab only; removed once it has been used. */
export const RESUME_TOKEN_KEY = "tattooos.bookingDraftResume";

function isNotFound(error: unknown): boolean {
  return typeof error === "object" && error !== null && "status" in error
    && (error as FetchBaseQueryError).status === 404;
}

/**
 * C05: where a "booking attempt" email's link lands. Someone booked as a guest with an email that already has an
 * account; the server kept what they typed as an UNCLAIMED draft. Following this link (the mailbox) and signing in
 * (the account) together attach it, and the person lands on the booking form, where #460's restore notice offers
 * it and says which answer was not kept.
 *
 * The token is in the URL fragment, which no server ever sees. Signing in drops the fragment, so while signed out
 * the token waits in sessionStorage (this tab only) and is removed as soon as it has been used.
 */
export function BookingDraftResumePage() {
  useDocumentMeta({ title: "Continue your booking request — TattooOS", canonical: "/booking-drafts/resume" });
  const role = useAppSelector((s) => s.auth.role);
  const { hash, pathname, search } = useLocation();
  const [searchParams] = useSearchParams();
  const studioSlug = searchParams.get("studio");

  // The fragment wins when present; otherwise the copy kept across sign-in.
  const [token] = useState<string | null>(() => {
    const fromHash = readResumeTokenFromHash(hash);
    if (fromHash) sessionStorage.setItem(RESUME_TOKEN_KEY, fromHash);
    return fromHash ?? sessionStorage.getItem(RESUME_TOKEN_KEY);
  });

  let body: ReactNode;
  if (!token || !studioSlug) {
    body = <Message title="This link is incomplete">
      Open the link straight from the email. If you copied it, make sure you copied all of it.
    </Message>;
  } else if (!role) {
    const loginTo = `/login?${new URLSearchParams({ redirect: pathname + search }).toString()}`;
    body = (
      <div className="space-y-4">
        <p className="text-sm">Sign in with the email address the message was sent to, and we&apos;ll bring back what
          you typed.</p>
        <Button asChild className="w-full"><Link to={loginTo}>Sign in to continue</Link></Button>
        <p className="text-xs text-muted-foreground">
          Forgotten your password? Use &quot;Forgot password&quot; on the sign-in page, then open this link again.
        </p>
      </div>
    );
  } else if (role !== Role.Client) {
    body = <Message title="This link opens in a client account">
      You&apos;re signed in to a studio account. Booking requests are kept in client accounts, so this one can&apos;t be
      picked up here.
    </Message>;
  } else {
    body = <Resume token={token} studioSlug={studioSlug} />;
  }

  const content = (
    <div className="flex-1 flex items-start justify-center px-4 py-12">
      <div className="w-full max-w-md">
        <Card>
          <CardHeader className="pb-2">
            <div className="flex items-center gap-2">
              <History className="h-4 w-4 text-info-text" aria-hidden="true" />
              <h1 className="text-base font-semibold tracking-tight">Continue your booking request</h1>
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

function Resume({ token, studioSlug }: { token: string; studioSlug: string }) {
  const navigate = useNavigate();
  const { data: studio, isError: studioMissing } = useGetPublicStudioQuery(studioSlug);
  const { ensure } = useEnsureActiveStudio();
  const [resume, { data, error, isError }] = useResumeBookingDraftMutation();
  const [switchFailed, setSwitchFailed] = useState(false);
  const started = useRef(false);

  // Approved useEffect: a one-shot ACTION, not a data fetch. A draft belongs to one studio and is read under the
  // tenant filter, so the session switches to that studio first (as /book?studio=<slug> does), then claims.
  useEffect(() => {
    if (!studio || started.current) return;
    started.current = true;
    void ensure(studio.studioId).then((ok) => {
      if (!ok) { setSwitchFailed(true); return; }
      void resume(token);
    });
  }, [studio, ensure, resume, token]);

  const settled = data !== undefined || isError;
  // Approved useEffect: the token is used once the server has answered, whatever it said.
  useEffect(() => {
    if (settled) sessionStorage.removeItem(RESUME_TOKEN_KEY);
  }, [settled]);

  const bookPath = `/book?studio=${encodeURIComponent(studioSlug)}`;

  if (studioMissing || (isError && isNotFound(error))) {
    return <Message title="This link can't be used">
      It may be from an older email, the request may already have been sent, or you may be signed in with a different
      account from the one the email went to. You can still book from the start.
      <BookLink to={bookPath} />
    </Message>;
  }
  if (switchFailed || isError) {
    return <Message title="We couldn't open your booking request">
      Please open the link from the email again in a moment.
    </Message>;
  }
  if (!data) {
    return <div className="space-y-3" role="status" aria-label="Opening your booking request">
      <Skeleton className="h-5 w-2/3" />
      <Skeleton className="h-16 w-full" />
    </div>;
  }
  return <Outcome result={data} bookPath={bookPath} onContinue={() => navigate(bookPath)} />;
}

function Outcome({ result, bookPath, onContinue }: { result: ResumeBookingDraftResponse; bookPath: string; onContinue: () => void }) {
  switch (result.outcome) {
    case "Resumed":
      // The booking form's restore notice takes it from here (and says which answer was not kept).
      return <Navigate to={bookPath} replace />;
    case "AlreadyClaimed":
      return (
        <div className="space-y-3">
          <p className="text-sm font-medium">You&apos;ve already picked this request up</p>
          <p className="text-sm text-muted-foreground">It&apos;s waiting for you on the booking form.</p>
          <Button className="w-full" onClick={onContinue}>Go to the booking form</Button>
        </div>
      );
    case "Expired":
      return <Message title="This link has expired">
        Links in these emails work for seven days, and what was typed is no longer kept. You can still book from the
        start.
        <BookLink to={bookPath} />
      </Message>;
  }
}

function BookLink({ to }: { to: string }) {
  return <span className="mt-3 block"><Button asChild variant="outline" className="w-full"><Link to={to}>Book at this studio</Link></Button></span>;
}

function Message({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="space-y-2" role="status">
      <p className="text-sm font-medium">{title}</p>
      <div className="text-sm text-muted-foreground">{children}</div>
    </div>
  );
}
