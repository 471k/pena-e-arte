import { PublicContentLayout } from "./PublicContentLayout";

// Structural placeholder. Full sectioned content + lawyer-review banner land in
// Phase 2 (PENA-102). Real legal text is an open question for the founder.
export function PrivacyPolicyPage() {
  return (
    <PublicContentLayout title="Privacy Policy — TattooOS" canonicalPath="/privacy">
      <h1 className="text-2xl font-semibold tracking-tight">Privacy Policy</h1>
      <p className="mt-3 text-muted-foreground">
        How TattooOS collects, uses, and protects your personal data.
      </p>
    </PublicContentLayout>
  );
}
