import { useEffect } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { Loader2, ChevronLeft } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useGetPublicArtistQuery } from "../publicApi";

export function ArtistPortfolioPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const navigate       = useNavigate();
  const { data: artist, isLoading, isError } = useGetPublicArtistQuery(slug, { skip: !slug });

  useEffect(() => {
    if (artist) {
      document.title = `${artist.name} — Tattoo Artist on Pena e Artë`;
    }
  }, [artist]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (isError || !artist) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Artist not found.</p>
        <Button variant="outline" onClick={() => navigate("/")}>Go home</Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-2xl mx-auto px-4 py-8 space-y-6">
        <Link
          to={`/s/${artist.studioSlug}`}
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ChevronLeft className="h-3.5 w-3.5" />
          {artist.studioName}
        </Link>

        <div className="space-y-2">
          <h1 className="text-2xl font-bold tracking-tight">{artist.name}</h1>
          {artist.bio && (
            <p className="text-sm text-muted-foreground whitespace-pre-wrap">{artist.bio}</p>
          )}
        </div>

        {artist.showBookingCta && (
          <Button
            className="w-full"
            onClick={() => navigate(`/book?studio=${artist.studioSlug}&artist=${artist.slug}`)}
          >
            Book with {artist.name}
          </Button>
        )}

        {artist.portfolioImages.length > 0 && (
          <div className="space-y-3">
            <p className="text-sm font-medium">Portfolio</p>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
              {artist.portfolioImages.map((url, i) => (
                <a key={i} href={url} target="_blank" rel="noopener noreferrer">
                  <img
                    src={url}
                    alt={`Portfolio image ${i + 1}`}
                    className="w-full aspect-square object-cover rounded-md hover:opacity-90 transition-opacity"
                    loading="lazy"
                  />
                </a>
              ))}
            </div>
          </div>
        )}
      </div>

      <footer className="py-3 text-center text-xs text-muted-foreground border-t mt-8">
        <a href="https://penaearte.com" target="_blank" rel="noopener noreferrer" className="hover:underline">
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
