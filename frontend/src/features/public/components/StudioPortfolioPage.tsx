import { useParams, Link } from "react-router-dom";
import { MapPin, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetPublicStudioQuery, type PublicArtistSummary } from "../publicApi";
import { useAppSelector } from "@/app/hooks";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { ReviewSection } from "./ReviewSection";

function ArtistCard({ artist }: { artist: PublicArtistSummary }) {
  return (
    <Link to={`/artist/${artist.slug}`}>
      <Card className="hover:border-ring transition-colors cursor-pointer">
        <CardContent className="p-4 space-y-1">
          <p className="font-medium text-sm">{artist.name}</p>
          {artist.bio && (
            <p className="text-xs text-muted-foreground line-clamp-2">{artist.bio}</p>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

function StudioMeta({ name, slug, description, coverImageUrl }: {
  name: string;
  slug: string;
  description: string | null;
  coverImageUrl: string | null;
}) {
  useDocumentMeta({
    title:       `${name} — Book a Tattoo on Pena e Artë`,
    description: description ?? `Book your next tattoo at ${name}.`,
    ogImage:     coverImageUrl ?? undefined,
    canonical:   `https://penaearte.com/s/${slug}`,
  });
  return null;
}

export function StudioPortfolioPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const token = useAppSelector((s) => s.auth.token);
  const { data: studio, isLoading, isError } = useGetPublicStudioQuery(slug, { skip: !slug });

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading studio page">
        <Skeleton className="h-48 w-full" />
        <div className="max-w-2xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-10 w-full rounded-md" />
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Skeleton className="h-20 w-full rounded-lg" />
            <Skeleton className="h-20 w-full rounded-lg" />
          </div>
        </div>
      </div>
    );
  }

  if (isError || !studio) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Studio not found.</p>
        <Button variant="outline" asChild>
          <Link to="/">Go home</Link>
        </Button>
      </div>
    );
  }

  const bookUrl = `/book?studio=${studio.slug}`;
  const ctaUrl  = token ? bookUrl : `/login?redirect=${encodeURIComponent(bookUrl)}`;

  return (
    <div className="min-h-screen bg-background">
      <StudioMeta
        name={studio.name}
        slug={studio.slug}
        description={studio.description}
        coverImageUrl={studio.coverImageUrl}
      />

      {studio.coverImageUrl && (
        <div className="h-48 bg-muted overflow-hidden">
          <img
            src={studio.coverImageUrl}
            alt={studio.name}
            className="w-full h-full object-cover"
          />
        </div>
      )}

      <div className="max-w-2xl mx-auto px-4 py-8 space-y-6">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold tracking-tight">{studio.name}</h1>
          <div className="flex items-center gap-1 text-sm text-muted-foreground">
            <MapPin className="h-3.5 w-3.5" />
            <span>{studio.city}</span>
          </div>
          {studio.description && (
            <p className="text-sm text-muted-foreground">{studio.description}</p>
          )}
        </div>

        {studio.showBookingCta && (
          <Button className="w-full" asChild>
            <Link to={ctaUrl}>Book here</Link>
          </Button>
        )}

        {studio.artists.length > 0 && (
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <Users className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium">Our artists</span>
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {studio.artists.map((a) => (
                <ArtistCard key={a.artistId} artist={a} />
              ))}
            </div>
          </div>
        )}

        <ReviewSection slug={studio.slug} target="studio" token={token} />
      </div>

      <footer className="py-3 text-center text-xs text-muted-foreground border-t mt-8">
        <a href="https://penaearte.com" target="_blank" rel="noopener noreferrer" className="hover:underline">
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
