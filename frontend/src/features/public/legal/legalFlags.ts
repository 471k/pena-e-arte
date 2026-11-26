// The per-document 'final legal copy' flags, kept out of LawyerReviewBanner.tsx so that file
// exports only a component (react-refresh/only-export-components).

export type LegalDocId = "terms" | "clientTerms" | "privacy";

// One flag per document, so each page can go live (or be pulled back) on its own.
// All three were signed off by the reviewing lawyer on 2026-11-25. Set a flag to `false`
// to bring the amber warning back on that page while its text is being revised.
// See claude-shared/legal/lawyer-pack-platform-terms-filled-2026-11-25.md §5 for the sign-off record.
export const FINAL_LEGAL_COPY: Record<LegalDocId, boolean> = {
  terms: true,
  clientTerms: true,
  privacy: true,
};

// True only when every document is final. Kept so existing imports keep working.
export const HAS_FINAL_LEGAL_COPY: boolean = Object.values(FINAL_LEGAL_COPY).every(Boolean);
