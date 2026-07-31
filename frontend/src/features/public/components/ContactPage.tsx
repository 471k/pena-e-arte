import { PublicContentLayout } from "./PublicContentLayout";

// Structural placeholder. Phase 2 (PENA-102) decides monitored-inbox vs. form
// (open question §3.5) and fills content accordingly.
export function ContactPage() {
  return (
    <PublicContentLayout title="Contact — TattooOS" canonicalPath="/contact">
      <h1 className="text-2xl font-semibold tracking-tight">Contact</h1>
      <p className="mt-3 text-muted-foreground">Get in touch with the TattooOS team.</p>
    </PublicContentLayout>
  );
}
