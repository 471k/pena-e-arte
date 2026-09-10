import { ShieldX } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppSelector } from "@/app/hooks";
import type { StudioResponse } from "@/features/studios/studiosApi";

type SuspensionBannerProps = {
  studio?: StudioResponse;
  role?:   "owner" | "artist" | "client";
};

// Defined outside the component so the impure Date.now() read happens in a plain helper, not
// directly in render — matches SubscriptionOversightPage.tsx's daysPastDue helper.
function daysPastDueFrom(pastDueSince: string): number {
  return Math.max(1, Math.floor((Date.now() - new Date(pastDueSince).getTime()) / (1000 * 60 * 60 * 24)));
}

export function SuspensionBanner({ studio, role = "owner" }: SuspensionBannerProps) {
  const studioSuspended = useAppSelector((s) => s.ui.studioSuspended);

  const isSuspended = studio?.isActive === false || studioSuspended;
  if (!isSuspended) return null;

  // Only the owner branch gets PastDue-specific copy — artist/client messages stay generic
  // regardless of the underlying subscription status, since neither role can act on billing.
  const isPastDue = role === "owner" && studio?.subscriptionStatus === "PastDue" && !!studio?.pastDueSince;
  const daysPastDue = isPastDue ? daysPastDueFrom(studio!.pastDueSince!) : 0;

  const message =
    role === "artist"
      ? "Your studio's account has been suspended by the platform. Contact your studio owner or platform support to resolve this."
      : role === "client"
      ? "This studio's account has been suspended. Your bookings and records are safe, but access is temporarily unavailable. Contact the studio for assistance."
      : isPastDue
      ? `Your subscription payment is ${daysPastDue} day${daysPastDue === 1 ? "" : "s"} overdue. Update your billing details to avoid service interruption.`
      : "Your studio has been suspended by the platform administrator. Contact support or reactivate your subscription to resolve this.";

  return (
    <div
      role="alert"
      aria-live="polite"
      className="flex items-center gap-3 px-4 py-2.5 bg-red-500/10 border-b border-red-500/30 text-red-700 dark:text-red-400 text-sm"
    >
      <ShieldX className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span className="flex-1">
        {message}
        {role === "owner" && (
          <>
            {" "}
            <a
              href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@tattooos.co"}`}
              className="font-medium underline underline-offset-4"
            >
              Contact support
            </a>{" "}
            or{" "}
            <Link to="/subscribe" className="font-medium underline underline-offset-4">
              reactivate your subscription
            </Link>
            .
          </>
        )}
      </span>
    </div>
  );
}
