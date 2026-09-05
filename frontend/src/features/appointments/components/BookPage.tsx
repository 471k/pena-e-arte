import { Link, Navigate, useSearchParams } from "react-router-dom";
import { AlertTriangle, ChevronLeft, X } from "lucide-react";
import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { BookingWidget } from "@/features/booking/components/BookingWidget";
import { GuestBookAppointmentForm } from "@/features/booking/components/GuestBookAppointmentForm";
import { PublicPageHeader } from "@/features/public/components/PublicPageHeader";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";
import { useResendVerificationEmailMutation } from "@/features/auth/authApi";
import { Role } from "@/shared/types/roles";
import { getRoleRedirectPath } from "@/app/router";
import { BookAppointmentForm } from "./BookAppointmentForm";
import { MyBookingsSection } from "./MyBookingsSection";

// Unauthenticated visitor with a target studio (?studio=<slug>) — true guest checkout
// (Decision #1). No verification banner (nothing to verify yet), no MyBookingsSection
// (nothing to show pre-account).
function GuestBookPage({ slug }: { slug: string }) {
  useDocumentMeta({ title: "Book — TattooOS", canonical: "/book" });

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <PublicPageHeader />
      <div className="flex-1 flex items-start justify-center px-4 py-12">
        <div className="w-full max-w-md space-y-6">
          <Card>
            <CardHeader className="pb-2">
              <h1 className="text-base font-semibold tracking-tight">Book an appointment</h1>
              <p className="text-xs text-muted-foreground">
                Select an artist, date, and appointment duration. Your booking is a
                request — the studio will confirm within 24 hours.
              </p>
            </CardHeader>
            <CardContent>
              <GuestBookAppointmentForm slug={slug} />
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}

// Unauthenticated visitor with no ?studio=<slug> — nothing to book against. Same empty
// state BookAppointmentForm already shows a studio-less authenticated client (Part 5c).
function NoStudioBookPage() {
  useDocumentMeta({ title: "Book — TattooOS", canonical: "/book" });

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <PublicPageHeader />
      <div className="flex-1 flex items-center justify-center px-4 py-12">
        <div className="flex flex-col items-center justify-center gap-3 text-center">
          <p className="text-sm text-muted-foreground">
            You haven&apos;t chosen a studio yet. Browse studios to book your first appointment.
          </p>
          <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
            <Link to="/discover">Browse studios</Link>
          </Button>
        </div>
      </div>
    </div>
  );
}

export function BookPage() {
  const role = useAppSelector((s) => s.auth.role);
  const [searchParams] = useSearchParams();
  const studioSlug = searchParams.get("studio");

  // Unauthenticated: guest checkout branch (Decision #1/#13, Part 5c).
  if (!role) {
    return studioSlug ? <GuestBookPage slug={studioSlug} /> : <NoStudioBookPage />;
  }

  // Preserve the pre-existing router-level restriction (RoleGuard allowedRoles=[Client,
  // Issuer]) now that /book is reachable outside that guard — Artist/Owner still redirect
  // to their own home, unchanged from before this feature.
  if (role !== Role.Client && role !== Role.Issuer) {
    return <Navigate to={getRoleRedirectPath(role)} replace />;
  }

  return <AuthenticatedBookPage />;
}

function AuthenticatedBookPage() {
  useDocumentMeta({ title: "Book — TattooOS", canonical: "/book" });

  const user        = useAppSelector((s) => s.auth.user);
  const needsVerify = user != null && user.emailVerified === false;
  const [dismissed, setDismissed] = useState(false);
  const [resend, { isLoading: isResending, isSuccess: resentOk }] =
    useResendVerificationEmailMutation();

  return (
    <BookingWidget>
      <div className="bg-background flex items-start justify-center px-4 py-12">
        <div className="w-full max-w-md space-y-6">

          <Link
            to="/"
            className="inline-flex items-center gap-1 text-xs text-muted-foreground
                       hover:text-foreground transition-colors"
          >
            <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            Back
          </Link>

          {needsVerify && !dismissed && (
            <div
              role="alert"
              // Matches the established alert-banner pattern used elsewhere (ReadOnlyBanner,
              // LoginPage, DashboardPage's SubscriptionBanner): a mid-tone amber-500 at low
              // opacity for border/bg works in both themes unmodified, with only the TEXT
              // needing a light/dark split. The previous amber-950/amber-800/amber-300
              // combination here was tuned for dark mode only — in light mode (now actually
              // exercised by e2e's chromium-light project) it rendered near-invisible pale
              // yellow text on a muddy tan background (measured 1.03:1 and 1.14:1, both far
              // under WCAG AA's 4.5:1). Fixed 2026-09-05.
              className="relative rounded-lg border border-amber-500/30 bg-amber-500/10
                         px-4 py-3 text-sm flex items-start gap-3"
            >
              <AlertTriangle
                className="h-4 w-4 mt-0.5 shrink-0 text-amber-700 dark:text-amber-400"
                aria-hidden="true"
              />
              <div className="flex-1 space-y-1">
                {resentOk ? (
                  <p className="text-amber-700 dark:text-amber-400">
                    Verification email sent — check your inbox.
                  </p>
                ) : (
                  <>
                    <p className="text-amber-700 dark:text-amber-400">
                      Please verify your email address to complete bookings.
                    </p>
                    <button
                      type="button"
                      onClick={() => void resend()}
                      disabled={isResending}
                      className="text-xs text-amber-800 hover:text-amber-900 dark:text-amber-400
                                 dark:hover:text-amber-300 underline underline-offset-2
                                 transition-colors disabled:opacity-60"
                    >
                      {isResending ? "Sending…" : "Resend verification email"}
                    </button>
                  </>
                )}
              </div>
              <button
                type="button"
                onClick={() => setDismissed(true)}
                aria-label="Dismiss email verification reminder"
                className="text-amber-700/60 hover:text-amber-700 dark:text-amber-400/60
                           dark:hover:text-amber-400 transition-colors"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          )}

          <Card>
            <CardHeader className="pb-2">
              <h1 className="text-base font-semibold tracking-tight">Book an appointment</h1>
              <p className="text-xs text-muted-foreground">
                Select an artist, date, and appointment duration. Your booking is a
                request — the studio will confirm within 24 hours.
              </p>
            </CardHeader>
            <CardContent>
              <BookAppointmentForm />
            </CardContent>
          </Card>

          <MyBookingsSection />
        </div>
      </div>
    </BookingWidget>
  );
}
