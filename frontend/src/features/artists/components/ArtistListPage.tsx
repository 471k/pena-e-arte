import { useEffect, useState } from "react";
import { Loader2, PenLine, Search, Users } from "lucide-react";
import { Input } from "@/shared/components/ui/input";
import { useGetArtistsQuery } from "../artistsApi";
import { ArtistCard } from "./ArtistCard";

export function ArtistListPage() {
  const [inputValue, setInputValue] = useState("");
  const [search, setSearch] = useState<string | undefined>(undefined);

  useEffect(() => {
    const id = setTimeout(() => setSearch(inputValue.trim() || undefined), 300);
    return () => clearTimeout(id);
  }, [inputValue]);

  const { data: artists, isLoading, isError } = useGetArtistsQuery(search);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Artists</span>
        </div>
        {artists && (
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <Users className="h-3.5 w-3.5" />
            <span>{artists.length} artist{artists.length !== 1 ? "s" : ""}</span>
          </div>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
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
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading artists…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load artists. Please try again.
          </p>
        )}

        {!isLoading && !isError && artists?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            {search ? `No artists match "${search}".` : "No artists in this studio yet."}
          </p>
        )}

        {!isLoading && !isError && artists && artists.length > 0 && (
          <div className="space-y-2">
            {artists.map((artist) => (
              <ArtistCard key={artist.id} artist={artist} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
