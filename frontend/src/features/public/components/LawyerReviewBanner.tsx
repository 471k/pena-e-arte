import { AlertTriangle } from "lucide-react";
import { FINAL_LEGAL_COPY, type LegalDocId } from "../legal/legalFlags";

export function LawyerReviewBanner({ doc }: { doc: LegalDocId }) {
  if (FINAL_LEGAL_COPY[doc]) return null;

  return (
    <div
      role="alert"
      className="mb-6 flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-warning-text dark:text-amber-300"
    >
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <p>
        <strong>[LAWYER REVIEW REQUIRED]</strong> This page is being revised and is
        pending review by a qualified Albanian data-protection lawyer.
      </p>
    </div>
  );
}
