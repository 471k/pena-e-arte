import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Search, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetArtistsQuery } from "../artistsApi";
import type { ArtistResponse } from "../artistsApi";

function ArtistRowSkeleton() {
  return (
    <div className="flex items-center gap-4 py-3 border-b">
      <Skeleton className="h-4 w-32" />
      <Skeleton className="h-4 w-48" />
      <Skeleton className="h-4 w-36" />
    </div>
  );
}

export function ArtistListPage() {
  const navigate   = useNavigate();
  const canManage  = usePermission(Role.Owner);
  const [inputValue, setInputValue] = useState("");
  const [search, setSearch]         = useState<string | undefined>(undefined);

  useEffect(() => {
    const id = setTimeout(() => setSearch(inputValue.trim() || undefined), 300);
    return () => clearTimeout(id);
  }, [inputValue]);

  const { data: artists, isLoading, isError } = useGetArtistsQuery(search);

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
          {canManage && (
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

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load artists. Please try again.
          </p>
        )}

        {!isLoading && !isError && (
          <DataTable<ArtistResponse>
            columns={[
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
              { header: "Email",           accessorKey: "email" },
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
            ]}
            data={artists ?? []}
            keyExtractor={(a) => a.id}
            onRowClick={(a) => navigate(`/artists/${a.id}`)}
            emptyMessage={search ? `No artists match "${search}".` : "No artists in this studio yet."}
          />
        )}
      </main>
    </div>
  );
}
