import { useParams, Link } from "react-router-dom";
import { ChevronLeft } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useAppSelector } from "@/app/hooks";
import { useGetPublicArtistQuery } from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

function ArtistMeta({ name, slug, bio, coverImage }: {
  name: string;
  slug: string;
  bio: string | null;
  coverImage?: string;
}) {
  useDocumentMeta({
    title:       `${name} — Tattoo Artist on Pena e Artë`,
    description: bio ?? `View the portfolio of ${name}.`,
    ogImage:     coverImage,
    canonical:   `https://penaearte.com/artist/${slug}`,
  });
  return null;
}

export function ArtistPortfolioPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const token = useAppSelector((s) => s.auth.token);
  const { data: artist, isLoading, isError } = useGetPublicArtistQuery(slug, { skip: !slug });

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading artist page">
        <div className="max-w-2xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-10 w-full rounded-md" />
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="aspect-square w-full rounded-md" />
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (isError || !artist) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Artist not found.</p>
        <Button variant="outline" asChild>
          <Link to="/">Go home</Link>
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <ArtistMeta
        name={artist.name}
        slug={artist.slug}
        bio={artist.bio}
        coverImage={artist.portfolioImages[0]}
      />

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

        {artist.showBookingCta && (() => {
          const bookUrl = `/book?studio=${artist.studioSlug}&artist=${artist.slug}`;
          const ctaUrl  = token ? bookUrl : `/login?redirect=${encodeURIComponent(bookUrl)}`;
          return (
            <Button className="w-full" asChild>
              <Link to={ctaUrl}>Book with {artist.name}</Link>
            </Button>
          );
        })()}

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
