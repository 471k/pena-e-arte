import { useMemo, useState } from "react";
import { Palette, Plus, Search } from "lucide-react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
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
  const navigate = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const [searchParams] = useSearchParams();
  const clientId = searchParams.get("clientId") ?? undefined;
  const artistId = searchParams.get("artistId") ?? undefined;

  const { data: designs, isLoading, isError } = useGetDesignsQuery({ clientId, artistId });

  const [search, setSearch] = useState("");

  const filteredDesigns = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return designs ?? [];
    return (designs ?? []).filter((d) =>
      d.title.toLowerCase().includes(term),
    );
  }, [designs, search]);

  const hasDesigns = (designs?.length ?? 0) > 0;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Palette className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Designs</span>
        </div>
        <div className="flex items-center gap-3">
          {designs && (
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <Palette className="h-3.5 w-3.5" />
              <span>
                {designs.length} design{designs.length !== 1 ? "s" : ""}
              </span>
            </div>
          )}
          {canCreate && (
            <Button size="sm" onClick={() => navigate("/designs/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Design
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
          <Input
            placeholder="Search by title…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>

        {isLoading && (
          <div className="space-y-2" aria-label="Loading designs">
            {Array.from({ length: 5 }).map((_, i) => (
              <DesignCardSkeleton key={i} />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load designs. Please try again.
          </p>
        )}

        {!isLoading && !isError && !hasDesigns && (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <Palette className="h-8 w-8 text-muted-foreground/40" />
            <p className="text-sm font-medium">No designs yet</p>
            <p className="text-xs text-muted-foreground">
              Upload a tattoo design to start tracking approvals.
            </p>
            {canCreate && (
              <Button
                size="sm"
                onClick={() => navigate("/designs/new")}
                className="gap-1.5 mt-1"
                data-testid="empty-state-new-design"
              >
                <Plus className="h-3.5 w-3.5" />
                New Design
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && hasDesigns && filteredDesigns.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-8">
            No designs match &ldquo;{search}&rdquo;.
          </p>
        )}

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
