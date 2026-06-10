import { Palette, Plus } from "lucide-react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetDesignsQuery } from "../designsApi";
import { DesignCard } from "./DesignCard";

export function DesignListPage() {
  const navigate = useNavigate();
  const canCreate = usePermission(Role.Artist);
  const [searchParams] = useSearchParams();
  const clientId = searchParams.get("clientId") ?? undefined;
  const artistId = searchParams.get("artistId") ?? undefined;

  const { data: designs, isLoading, isError } = useGetDesignsQuery({ clientId, artistId });

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

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-lg" />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load designs. Please try again.
          </p>
        )}

        {!isLoading && !isError && designs?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No designs in this studio yet.
          </p>
        )}

        {!isLoading && !isError && designs && designs.length > 0 && (
          <div className="space-y-2">
            {designs.map((design) => (
              <DesignCard key={design.id} design={design} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
