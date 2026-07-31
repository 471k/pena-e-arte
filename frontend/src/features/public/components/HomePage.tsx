import { PublicContentLayout } from "./PublicContentLayout";

// Minimal first-touch public landing surface. Enriched in Phase 2 (PENA-102).
export function HomePage() {
  return (
    <PublicContentLayout
      title="TattooOS — booking & studio management for tattoo shops"
      description="TattooOS — booking & studio management for tattoo shops"
      canonicalPath="/"
    >
      <h1 className="text-2xl font-semibold tracking-tight">TattooOS</h1>
      <p className="mt-3 text-muted-foreground">
        Booking, deposits, consent forms, and studio management for tattoo shops.
      </p>
    </PublicContentLayout>
  );
}
