import { useParams } from "react-router-dom";
import { CalendarDays, MapPin } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetPublicStudioQuery, type PublicArtistSummary } from "../publicApi";

const EMBED_BASE = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;

function EmbedSkeleton() {
  return (
    <div className="min-h-screen bg-background flex flex-col" aria-label="Loading booking widget">
      <Skeleton className="h-32 w-full rounded-none" />
      <div className="flex-1 px-4 py-5 space-y-4">
        <div className="space-y-2">
          <Skeleton className="h-5 w-36" />
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-3 w-full" />
        </div>
        <Skeleton className="h-10 w-full rounded-md" />
        <div className="space-y-2">
          <Skeleton className="h-3 w-16" />
          <Skeleton className="h-12 w-full rounded-md" />
          <Skeleton className="h-12 w-full rounded-md" />
        </div>
      </div>
    </div>
  );
}

function isEmbedded(): boolean {
  try {
    return window.self !== window.top;
  } catch {
    return true;
  }
}

function ArtistPill({ artist }: { artist: PublicArtistSummary }) {
  return (
    <div className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm">
      <div className="h-7 w-7 rounded-full bg-muted flex items-center justify-center text-xs font-medium shrink-0">
        {artist.name.charAt(0).toUpperCase()}
      </div>
      <div className="min-w-0">
        <p className="font-medium leading-tight truncate">{artist.name}</p>
        {artist.bio && (
          <p className="text-xs text-muted-foreground line-clamp-1">{artist.bio}</p>
        )}
      </div>
    </div>
  );
}

export function EmbedPage() {
  const { studioSlug = "" } = useParams<{ studioSlug: string }>();
  const embedded = isEmbedded();
  const { data: studio, isLoading, isError } = useGetPublicStudioQuery(studioSlug, { skip: !studioSlug });

  if (isLoading) return <EmbedSkeleton />;

  if (isError || !studio) {
    return (
      <div
        className="flex items-center justify-center min-h-screen bg-background"
        role="alert"
        aria-live="polite"
      >
        <p className="text-sm text-muted-foreground">Studio not found.</p>
      </div>
    );
  }

  const studioPageUrl = `${EMBED_BASE}/s/${studio.slug}`;

  function handleBook() {
    if (embedded) {
      window.open(studioPageUrl, "_blank", "noopener,noreferrer");
    } else {
      window.location.href = studioPageUrl;
    }
  }

  return (
    <div className="min-h-screen bg-background flex flex-col">
      {studio.coverImageUrl && (
        <div className="h-32 bg-muted overflow-hidden shrink-0">
          <img
            src={studio.coverImageUrl}
            alt={studio.name}
            className="w-full h-full object-cover"
          />
        </div>
      )}

      <div className="flex-1 px-4 py-5 space-y-4">
        <div className="space-y-1">
          <h1 className="text-lg font-semibold tracking-tight">{studio.name}</h1>
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3" />
            <span>{studio.city}</span>
          </div>
          {studio.description && (
            <p className="text-xs text-muted-foreground">{studio.description}</p>
          )}
        </div>

        {studio.showBookingCta && (
          <Button className="w-full gap-2" onClick={handleBook}>
            <CalendarDays className="h-4 w-4" />
            Book an Appointment
          </Button>
        )}

        <div className="space-y-2">
          <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">Our artists</p>
          {studio.artists.length > 0 ? (
            <div className="space-y-2">
              {studio.artists.map((a) => (
                <ArtistPill key={a.artistId} artist={a} />
              ))}
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">Artists being added soon.</p>
          )}
        </div>
      </div>

      <footer className="px-4 py-3 border-t text-center">
        <a
          href="https://penaearte.com"
          target="_blank"
          rel="noopener noreferrer"
          className="text-xs text-muted-foreground hover:underline"
        >
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
