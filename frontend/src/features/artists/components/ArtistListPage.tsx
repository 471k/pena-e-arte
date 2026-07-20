import { useEffect, useMemo, useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Pencil, Plus, Search, Trash2, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import type { ColumnDef } from "@/shared/components/DataTable";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { cn } from "@/shared/utils/cn";
import { useGetArtistsQuery, useDeleteArtistMutation } from "../artistsApi";
import { useGetPlanUsageQuery } from "@/features/billing/billingApi";
import type { ArtistResponse } from "../artistsApi";

function ArtistRowSkeleton() {
  return (
    <div
      className="flex items-center gap-3 px-3 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-7 w-7 rounded-full shrink-0" />
      <div className="flex-1 space-y-1.5">
        <Skeleton className="h-3.5 w-32" />
        <Skeleton className="h-3 w-44 opacity-60" />
      </div>
      <div className="flex items-center gap-1">
        <Skeleton className="h-5 w-16 rounded-full" />
        <Skeleton className="h-5 w-14 rounded-full" />
      </div>
    </div>
  );
}

export function ArtistListPage() {
  const navigate  = useNavigate();
  const canManage = usePermission(Role.Owner);
  const [inputValue, setInputValue] = useState("");
  const [search, setSearch]         = useState<string | undefined>(undefined);

  useEffect(() => {
    const id = setTimeout(() => setSearch(inputValue.trim() || undefined), 300);
    return () => clearTimeout(id);
  }, [inputValue]);

  const { data: artists, isLoading, isError } = useGetArtistsQuery(search);
  const errorMessage = useSuspensionAwareError(isError, "Failed to load artists. Please try again.");
  // Plan usage endpoint is OwnerOnly — skip for artist role to avoid a guaranteed 403.
  const { data: usage } = useGetPlanUsageQuery(undefined, { skip: !canManage });
  const [deleteArtist, { isLoading: isDeletingArtist }] = useDeleteArtistMutation();
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [selectedSpec, setSelectedSpec]       = useState<string | null>(null);

  const allSpecs = useMemo<string[]>(() => {
    if (!artists) return [];
    const set = new Set<string>();
    artists.forEach((a) => {
      if (a.specializations) {
        a.specializations
          .split(",")
          .map((s) => s.trim())
          .filter(Boolean)
          .forEach((s) => set.add(s));
      }
    });
    return [...set].sort();
  }, [artists]);

  const filteredArtists = useMemo<ArtistResponse[]>(() => {
    if (!artists) return [];
    if (!selectedSpec) return artists;
    return artists.filter((a) =>
      a.specializations
        ?.split(",")
        .map((s) => s.trim())
        .includes(selectedSpec),
    );
  }, [artists, selectedSpec]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setSelectedSpec(null);
  }, [search]);

  const hasArtists = (artists?.length ?? 0) > 0;

  const tableEmptyMessage = search
    ? `No artists match "${search}".`
    : selectedSpec
    ? `No artists with "${selectedSpec}" specialization.`
    : "No artists in this studio yet.";

  const columns: ColumnDef<ArtistResponse>[] = [
    {
      header: "Name",
      cell: (a) => (
        <div className="flex items-center gap-2">
          <div className="h-7 w-7 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0 select-none">
            {a.firstName[0]?.toUpperCase()}{a.lastName[0]?.toUpperCase()}
          </div>
          <span className="font-medium">{a.firstName} {a.lastName}</span>
        </div>
      ),
    },
    { header: "Email", accessorKey: "email" },
    {
      header: "Specializations",
      cell: (a) => {
        if (!a.specializations) {
          return <span className="text-muted-foreground/60">—</span>;
        }
        const chips = a.specializations
          .split(",")
          .map((s) => s.trim())
          .filter(Boolean);
        if (chips.length === 0) {
          return <span className="text-muted-foreground/60">—</span>;
        }
        return (
          <div className="flex flex-wrap gap-1">
            {chips.map((spec) => (
              <span
                key={spec}
                className="rounded-full bg-muted px-1.5 py-0.5 text-xs font-medium"
              >
                {spec}
              </span>
            ))}
          </div>
        );
      },
    },
    {
      header: "",
      cell: (a) => (
        <div
          className="flex items-center justify-end gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <Button
            variant="ghost"
            size="sm"
            className="h-7 text-xs gap-1"
            onClick={() => navigate(`/artists/${a.id}`)}
          >
            <Pencil className="h-3 w-3" />
            Edit
          </Button>

          {canManage && (
            confirmDeleteId === a.id ? (
              <div className="flex items-center gap-1.5">
                <span className="text-xs text-destructive whitespace-nowrap">
                  Delete {a.firstName} {a.lastName}?
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-6 text-xs"
                  onClick={() => setConfirmDeleteId(null)}
                >
                  Cancel
                </Button>
                <Button
                  variant="destructive"
                  size="sm"
                  className="h-6 text-xs"
                  disabled={isDeletingArtist}
                  onClick={async () => {
                    try {
                      await deleteArtist(a.id).unwrap();
                      toast.success("Artist deleted.");
                    } catch (err: unknown) {
                      const message =
                        (err as { data?: { message?: string } } | undefined)?.data?.message
                        ?? "Failed to delete artist.";
                      toast.error(message);
                    }
                    setConfirmDeleteId(null);
                  }}
                >
                  {isDeletingArtist ? "Deleting…" : "Confirm"}
                </Button>
              </div>
            ) : (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 text-xs gap-1 text-destructive hover:text-destructive hover:bg-destructive/10"
                onClick={() => setConfirmDeleteId(a.id)}
              >
                <Trash2 className="h-3 w-3" />
                Delete
              </Button>
            )
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Users className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Artists</span>
        </div>
        <div className="flex items-center gap-3">
          {artists && (
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <Users className="h-3.5 w-3.5" />
              <span>{artists.length} artist{artists.length !== 1 ? "s" : ""}</span>
            </div>
          )}
          {canManage && usage && usage.artists.max !== null && (
            <span
              className={cn(
                "text-xs",
                usage.artists.current >= usage.artists.max
                  ? "text-amber-600 dark:text-amber-400 font-medium"
                  : "text-muted-foreground",
              )}
            >
              {usage.artists.current} of {usage.artists.max} artists used
            </span>
          )}
          {canManage && (isLoading || hasArtists || !!search) && (
            <Button size="sm" onClick={() => navigate("/artists/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              New Artist
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
          <Input
            placeholder="Search by name or email…"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            className="pl-9"
          />
        </div>

        {isLoading && (
          <div className="space-y-0">
            {Array.from({ length: 6 }).map((_, i) => (
              <ArtistRowSkeleton key={i} />
            ))}
          </div>
        )}

        {errorMessage && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            {errorMessage}
          </p>
        )}

        {/* Rich empty state — only when there are zero artists and no search is active */}
        {!isLoading && !isError && !hasArtists && !search && (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <Users className="h-8 w-8 text-muted-foreground/40" />
            <p className="text-sm font-medium">No artists yet</p>
            <p className="text-xs text-muted-foreground">
              Add your first artist to get started.
            </p>
            {canManage && (
              <Button
                size="sm"
                onClick={() => navigate("/artists/new")}
                className="gap-1.5 mt-1"
              >
                <Plus className="h-3.5 w-3.5" />
                New Artist
              </Button>
            )}
          </div>
        )}

        {/* Spec filter pills + table — when artists exist or a search is active */}
        {!isLoading && !isError && (hasArtists || !!search) && (
          <>
            {allSpecs.length > 0 && (
              <div className="flex flex-wrap items-center gap-2" aria-label="Filter by specialization">
                {allSpecs.map((spec) => (
                  <button
                    key={spec}
                    type="button"
                    aria-pressed={selectedSpec === spec}
                    onClick={() => setSelectedSpec(selectedSpec === spec ? null : spec)}
                    className={cn(
                      "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
                      selectedSpec === spec
                        ? "border-foreground bg-foreground text-background"
                        : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
                    )}
                  >
                    {spec}
                  </button>
                ))}
              </div>
            )}

            <DataTable<ArtistResponse>
              columns={columns}
              data={filteredArtists}
              keyExtractor={(a) => a.id}
              onRowClick={(a) => navigate(`/artists/${a.id}`)}
              emptyMessage={tableEmptyMessage}
            />
          </>
        )}
      </main>
    </div>
  );
}
