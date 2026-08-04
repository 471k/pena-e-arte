import { AlertTriangle } from "lucide-react";

// Flip to `true` (or wire to config) once the Albanian data-protection lawyer's
// final Privacy Policy / Terms text lands. The banner is gated on this constant
// so the placeholder warning disappears automatically — no future PR has to
// remember to hand-delete it. See open question §3.4.
export const HAS_FINAL_LEGAL_COPY = false;

export function LawyerReviewBanner() {
  if (HAS_FINAL_LEGAL_COPY) return null;

  return (
    <div
      role="alert"
      className="mb-6 flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:text-amber-300"
    >
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <p>
        <strong>[LAWYER REVIEW REQUIRED]</strong> This page shows the structural
        outline only. The binding legal text is pending review by a qualified
        Albanian data-protection lawyer and is not yet final.
      </p>
    </div>
  );
}
