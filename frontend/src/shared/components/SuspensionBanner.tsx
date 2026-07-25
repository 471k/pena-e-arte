import { ShieldX } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppSelector } from "@/app/hooks";
import type { StudioResponse } from "@/features/studios/studiosApi";

type SuspensionBannerProps = {
  studio?: StudioResponse;
  role?:   "owner" | "artist" | "client";
};

export function SuspensionBanner({ studio, role = "owner" }: SuspensionBannerProps) {
  const studioSuspended = useAppSelector((s) => s.ui.studioSuspended);

  const isSuspended = studio?.isActive === false || studioSuspended;
  if (!isSuspended) return null;

  const message =
    role === "artist"
      ? "Your studio's account has been suspended by the platform. Contact your studio owner or platform support to resolve this."
      : role === "client"
      ? "This studio's account has been suspended. Your bookings and records are safe, but access is temporarily unavailable. Contact the studio for assistance."
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
