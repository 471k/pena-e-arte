import { Link } from "react-router-dom";
import { AlertTriangle, ChevronLeft, X } from "lucide-react";
import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { BookingWidget } from "@/features/booking/components/BookingWidget";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";
import { useResendVerificationEmailMutation } from "@/features/auth/authApi";
import { BookAppointmentForm } from "./BookAppointmentForm";
import { MyBookingsSection } from "./MyBookingsSection";

export function BookPage() {
  useDocumentMeta({ title: "Book — Pena e Artë", canonical: "/book" });

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
              className="relative rounded-lg border border-amber-800/50 bg-amber-950/20
                         px-4 py-3 text-sm flex items-start gap-3"
            >
              <AlertTriangle
                className="h-4 w-4 mt-0.5 shrink-0 text-amber-400"
                aria-hidden="true"
              />
              <div className="flex-1 space-y-1">
                {resentOk ? (
                  <p className="text-amber-300">
                    Verification email sent — check your inbox.
                  </p>
                ) : (
                  <>
                    <p className="text-amber-300">
                      Please verify your email address to complete bookings.
                    </p>
                    <button
                      type="button"
                      onClick={() => void resend()}
                      disabled={isResending}
                      className="text-xs text-amber-400 hover:text-amber-300 underline
                                 underline-offset-2 transition-colors disabled:opacity-60"
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
                className="text-amber-400/60 hover:text-amber-400 transition-colors"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          )}

          <Card>
            <CardHeader className="pb-2">
              <h1 className="text-base font-semibold tracking-tight">Book an appointment</h1>
              <p className="text-xs text-muted-foreground">
                Select an artist, date, and session length. Your booking is a
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
