import { ShieldX } from "lucide-react";
import type { StudioResponse } from "@/features/studios/studiosApi";

export function SuspensionBanner({ studio }: { studio?: StudioResponse }) {
  if (studio?.isActive !== false) return null;

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 bg-red-500/10 border-b border-red-500/30 text-red-700 dark:text-red-400 text-sm">
      <ShieldX className="h-4 w-4 shrink-0" />
      <span className="flex-1">
        Your studio has been suspended by the platform administrator. Contact{" "}
        <a
          href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@penaearte.com"}`}
          className="font-medium underline underline-offset-4"
        >
          support
        </a>{" "}
        to resolve this.
      </span>
    </div>
  );
}
