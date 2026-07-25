import { useMemo, useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Palette, Plus, Search } from "lucide-react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
import { ResourceEmptyState } from "@/shared/components/ResourceEmptyState";
import { Role } from "@/shared/types/roles";
import { useGetDesignsQuery } from "../designsApi";
import { DesignCard } from "./DesignCard";

function DesignCardSkeleton() {
  return (
    <div
      className="rounded-xl border bg-card p-4 flex items-start gap-4"
      aria-hidden="true"
    >
      <Skeleton className="h-10 w-10 rounded-lg shrink-0" />
      <div className="flex-1 space-y-2">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-3 w-56 opacity-60" />
        <Skeleton className="h-3 w-20 opacity-40" />
      </div>
      <Skeleton className="h-8 w-8 rounded-md shrink-0" />
    </div>
  );
}

export function DesignListPage() {
  useDocumentMeta({ title: "Designs — TattooOS", canonical: "/designs" });

  const navigate = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const [searchParams] = useSearchParams();
  const clientId = searchParams.get("clientId") ?? undefined;
  const artistId = searchParams.get("artistId") ?? undefined;

  const { data: designs, isLoading, isError } = useGetDesignsQuery({ clientId, artistId });
  const errorMessage = useSuspensionAwareError(isError, "Failed to load designs. Please try again.");

  const [search, setSearch] = useState("");

  const filteredDesigns = useMemo(() => {
    const term = search.trim().toLowerCase();
    const matching = term
      ? (designs ?? []).filter((d) => d.title.toLowerCase().includes(term))
      : (designs ?? []);

    // Designs awaiting a new revision from the artist are the most time-sensitive —
    // surface them first regardless of creation date.
    return [...matching].sort((a, b) => {
      const aUrgent = a.status === "ChangesRequested";
      const bUrgent = b.status === "ChangesRequested";
      if (aUrgent === bUrgent) return 0;
      return aUrgent ? -1 : 1;
    });
  }, [designs, search]);

  const hasDesigns = (designs?.length ?? 0) > 0;

  return (
    <div className="min-h-screen bg-background">
      {/* ── Header — shares max-w-4xl container with <main> so left edges align ── */}
      <header className="border-b bg-background sticky top-0 z-10">
        <div className="max-w-4xl mx-auto px-4 py-3 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Palette className="h-5 w-5" />
            <span className="font-semibold tracking-tight">Designs</span>
          </div>
          <div className="flex items-center gap-3">
            {designs && (
              <span className="text-xs text-muted-foreground">
                {designs.length} design{designs.length !== 1 ? "s" : ""}
              </span>
            )}
            {canCreate && (
              <Button
                size="sm"
                onClick={() => navigate("/designs/new")}
                className="gap-1.5"
                data-tour="artist-create-design-button"
              >
                <Plus className="h-3.5 w-3.5" />
                New Design
              </Button>
            )}
          </div>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {/* Search — hidden until there are designs to search */}
        {hasDesigns && (
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" aria-hidden="true" />
            <Input
              aria-label="Search designs by title"
              placeholder="Search by title…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9"
            />
          </div>
        )}

        {/* Loading */}
        {isLoading && (
          <div className="space-y-2" aria-label="Loading designs">
            {Array.from({ length: 5 }).map((_, i) => (
              <DesignCardSkeleton key={i} />
            ))}
          </div>
        )}

        {/* Error */}
        {errorMessage && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            {errorMessage}
          </p>
        )}

        {/* Empty state — role-aware copy + conditional CTA */}
        {!isLoading && !isError && !hasDesigns && (
          <ResourceEmptyState
            icon={<Palette className="h-8 w-8" />}
            heading="No designs yet"
            body={
              canCreate
                ? "Upload a tattoo design to start tracking approvals."
                : "Your artist will upload designs here for your approval."
            }
            action={
              canCreate ? (
                <Button
                  size="sm"
                  onClick={() => navigate("/designs/new")}
                  className="gap-1.5 mt-1"
                  data-testid="empty-state-new-design"
                  data-tour="artist-create-design-button"
                >
                  <Plus className="h-3.5 w-3.5" />
                  New Design
                </Button>
              ) : undefined
            }
          />
        )}

        {/* No search match */}
        {!isLoading && !isError && hasDesigns && filteredDesigns.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-8">
            No designs match &ldquo;{search}&rdquo;.
          </p>
        )}

        {/* List */}
        {!isLoading && !isError && filteredDesigns.length > 0 && (
          <div className="space-y-2">
            {filteredDesigns.map((design) => (
              <DesignCard key={design.id} design={design} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
