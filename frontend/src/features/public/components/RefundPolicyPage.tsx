import { PublicContentLayout } from "./PublicContentLayout";

// Structural placeholder. In Phase 2 (PENA-102) this page is populated with REAL
// copy derived from the live deposit/cancellation code (DepositRule.cs,
// DepositCalculator.cs, ClientCancellationPolicy.cs) — not aspirational text.
export function RefundPolicyPage() {
  return (
    <PublicContentLayout title="Refund Policy — TattooOS" canonicalPath="/refund-policy">
      <h1 className="text-2xl font-semibold tracking-tight">Refund Policy</h1>
      <p className="mt-3 text-muted-foreground">
        How deposits, cancellations, and no-shows are handled.
      </p>
    </PublicContentLayout>
  );
}
