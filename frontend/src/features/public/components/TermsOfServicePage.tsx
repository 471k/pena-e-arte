import { PublicContentLayout } from "./PublicContentLayout";

// Structural placeholder. Full sectioned content + lawyer-review banner land in
// Phase 2 (PENA-102).
export function TermsOfServicePage() {
  return (
    <PublicContentLayout title="Terms of Service — TattooOS" canonicalPath="/terms">
      <h1 className="text-2xl font-semibold tracking-tight">Terms of Service</h1>
      <p className="mt-3 text-muted-foreground">
        The terms governing your use of TattooOS.
      </p>
    </PublicContentLayout>
  );
}
