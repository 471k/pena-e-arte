import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  AtSign,
  ChevronLeft,
  ChevronRight,
  Images,
  MapPin,
  Phone,
  Users,
  X,
} from "lucide-react";
import { Button }            from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton }          from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
} from "@/shared/components/ui/dialog";
import { StarRating }              from "@/shared/components/ui/StarRating";
import { useAppSelector }          from "@/app/hooks";
import { useGetPublicStudioQuery } from "../publicApi";
import type { PublicArtistSummary } from "../publicApi";
import { useDocumentMeta }          from "@/shared/utils/useDocumentMeta";
import { useStructuredData }        from "@/shared/utils/useStructuredData";
import { ReviewSection }            from "./ReviewSection";
import { PublicPageHeader }         from "./PublicPageHeader";

function StudioMeta({
  name, slug, description, coverImageUrl, city, averageRating, reviewCount,
}: {
  name: string; slug: string; description: string | null; coverImageUrl: string | null;
  city: string; averageRating: number | null; reviewCount: number;
}) {
  useDocumentMeta({
    title:       `${name} — Book a Tattoo on Pena e Artë`,
    description: description ?? `Book your next tattoo at ${name}.`,
    ogImage:     coverImageUrl ?? undefined,
    canonical:   `https://penaearte.com/s/${slug}`,
  });
  useStructuredData({
    "@context":    "https://schema.org",
    "@type":       "TattooParlor",
    name,
    description:   description ?? undefined,
    url:           `https://penaearte.com/s/${slug}`,
    image:         coverImageUrl ?? undefined,
    address:       { "@type": "PostalAddress", addressLocality: city },
    ...(reviewCount > 0
      ? { aggregateRating: { "@type": "AggregateRating", ratingValue: averageRating, reviewCount } }
      : {}),
  });
  return null;
}

function ArtistAvatar({ name, profileImageUrl }: { name: string; profileImageUrl: string | null }) {
  if (profileImageUrl) {
    return (
      <img
        src={profileImageUrl}
        alt={`Profile photo of ${name}`}
        className="h-10 w-10 rounded-full object-cover shrink-0"
      />
    );
  }
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
  return (
    <div
      className="h-10 w-10 rounded-full bg-gradient-to-br from-zinc-700 to-zinc-800
                 shrink-0 flex items-center justify-center"
      aria-hidden="true"
    >
      <span className="text-xs font-bold text-white/25 select-none">{initials}</span>
    </div>
  );
}

function ArtistCard({ artist }: { artist: PublicArtistSummary }) {
  const primarySpec = artist.specializations?.split(",")[0]?.trim() ?? null;

  return (
    <Link
      to={`/artist/${artist.slug}`}
      aria-label={`View ${artist.name}'s portfolio`}
      className="block focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1
                 rounded-lg"
    >
      <Card
        className="hover:border-border/80 hover:shadow-sm hover:shadow-black/20
                   transition-all cursor-pointer group h-full"
      >
        <CardContent className="p-4 space-y-2.5">
          <div className="flex items-center gap-3">
            <ArtistAvatar
              name={artist.name}
              profileImageUrl={artist.profileImageUrl}
            />
            <div className="min-w-0 flex-1">
              <p className="font-semibold text-sm leading-tight truncate">
                {artist.name}
              </p>
              {primarySpec && (
                <p className="text-xs text-muted-foreground truncate mt-0.5">
                  {primarySpec}
                </p>
              )}
            </div>
            <ChevronRight
              className="h-4 w-4 text-muted-foreground/40 shrink-0
                         group-hover:text-foreground/70 transition-colors"
              aria-hidden="true"
            />
          </div>

          {artist.reviewCount > 0 && (
            <div className="flex items-center gap-1.5">
              <StarRating value={Math.round(artist.averageRating ?? 0)} />
              <span className="text-xs text-muted-foreground">
                ({artist.reviewCount})
              </span>
            </div>
          )}

          {!primarySpec && artist.bio && (
            <p className="text-xs text-muted-foreground line-clamp-2">{artist.bio}</p>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

function GalleryLightbox({
  imageUrl, studioName, onClose,
}: {
  imageUrl:   string | null;
  studioName: string;
  onClose:    () => void;
}) {
  return (
    <Dialog open={!!imageUrl} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent
        className="max-w-5xl w-full p-0 bg-black border-0 overflow-hidden"
        aria-label={`Portfolio image from ${studioName}`}
      >
        {imageUrl && (
          <div className="relative">
            <img
              src={imageUrl}
              alt={`Tattoo portfolio by ${studioName}`}
              className="w-full h-auto max-h-[90vh] object-contain"
            />
            <button
              type="button"
              onClick={onClose}
              aria-label="Close image"
              className="absolute top-3 right-3 rounded-full bg-black/60 backdrop-blur-sm
                         p-1.5 text-white hover:bg-black/80 transition-colors"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

function StudioPageSkeleton() {
  return (
    <div className="min-h-screen bg-background" aria-label="Loading studio page" aria-busy="true">
      {/* Header placeholder — matches real header height (~48px) */}
      <div className="h-[49px] border-b bg-background/95" aria-hidden="true" />

      <Skeleton className="h-72 w-full rounded-none" />

      <div className="max-w-6xl mx-auto px-4 py-8">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_300px] gap-8 lg:gap-12 items-start">
          <div className="space-y-6">
            <div className="space-y-3">
              <Skeleton className="h-8 w-56" />
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-4 w-20" />
              <Skeleton className="h-16 w-full" />
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-20 w-full rounded-lg" />
              ))}
            </div>
          </div>
          <div className="space-y-3">
            <Skeleton className="h-11 w-full rounded-md" />
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-32" />
          </div>
        </div>
      </div>
    </div>
  );
}

export function StudioPortfolioPage() {
  const { slug = "" }  = useParams<{ slug: string }>();
  const token          = useAppSelector((s) => s.auth.token);
  const role           = useAppSelector((s) => s.auth.role);
  const tenantId       = useAppSelector((s) => s.auth.tenantId);
  const [lightboxUrl,  setLightboxUrl] = useState<string | null>(null);

  const { data: studio, isLoading, isError } =
    useGetPublicStudioQuery(slug, { skip: !slug });

  if (isLoading) return <StudioPageSkeleton />;

  if (isError || !studio) {
    return (
      <div className="min-h-screen bg-background flex flex-col">
        <PublicPageHeader />
        <div className="flex flex-col items-center justify-center flex-1 gap-4">
          <p className="text-muted-foreground">Studio not found.</p>
          <Button variant="outline" asChild>
            <Link to="/discover">Browse studios</Link>
          </Button>
        </div>
      </div>
    );
  }

  const bookUrl = `/book?studio=${studio.slug}`;
  const ctaUrl  = token
    ? bookUrl
    : `/login?redirect=${encodeURIComponent(bookUrl)}&studioId=${studio.studioId}`;
  const canRespond = role === "owner" && tenantId === studio.studioId;

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <StudioMeta
        name={studio.name}
        slug={studio.slug}
        description={studio.description}
        coverImageUrl={studio.coverImageUrl}
        city={studio.city}
        averageRating={studio.averageRating}
        reviewCount={studio.reviewCount}
      />

      <GalleryLightbox
        imageUrl={lightboxUrl}
        studioName={studio.name}
        onClose={() => setLightboxUrl(null)}
      />

      <PublicPageHeader />

      {/* Hero */}
      <div className="relative h-72 bg-zinc-900 overflow-hidden">
        {studio.coverImageUrl ? (
          <img
            src={studio.coverImageUrl}
            alt={`${studio.name} cover`}
            className="w-full h-full object-cover"
          />
        ) : (
          <div
            className="w-full h-full bg-gradient-to-br from-zinc-800 to-zinc-900
                       flex items-center justify-center"
            aria-hidden="true"
          >
            <span className="text-6xl font-bold text-white/10 select-none">
              {studio.name.split(/\s+/).map((w) => w[0]).join("").slice(0, 2).toUpperCase()}
            </span>
          </div>
        )}
        <div
          className="absolute inset-0 bg-gradient-to-t from-black/70 via-black/20 to-transparent
                     flex flex-col justify-end px-6 py-6"
        >
          <h1 className="text-3xl font-bold text-white tracking-tight drop-shadow-lg">
            {studio.name}
          </h1>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8">
        <Link
          to="/discover"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground
                     hover:text-foreground transition-colors mb-6 block
                     py-2 -my-2 min-h-[44px]"
          aria-label="Back to studio discovery"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Browse studios
        </Link>

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_300px] gap-8 lg:gap-12 items-start">

          {/* Left: main content */}
          <div className="space-y-10">

            <div className="space-y-3">
              <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                <MapPin className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{studio.city}</span>
              </div>

              {studio.reviewCount > 0 && (
                <div className="flex items-center gap-2">
                  <StarRating value={Math.round(studio.averageRating ?? 0)} />
                  <span className="text-sm text-muted-foreground">
                    {studio.averageRating?.toFixed(1)} · {studio.reviewCount}{" "}
                    review{studio.reviewCount !== 1 ? "s" : ""}
                  </span>
                </div>
              )}

              {studio.description && (
                <p className="text-sm text-muted-foreground/90 leading-relaxed">
                  {studio.description}
                </p>
              )}
            </div>

            {studio.artists.length > 0 && (
              <section aria-labelledby="artists-heading">
                <div className="flex items-center gap-2.5 mb-4">
                  <h2
                    id="artists-heading"
                    className="text-lg font-semibold flex items-center gap-2"
                  >
                    <Users className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                    Artists
                  </h2>
                  <span className="text-sm text-muted-foreground">
                    ({studio.artists.length})
                  </span>
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {studio.artists.map((a) => (
                    <ArtistCard key={a.artistId} artist={a} />
                  ))}
                </div>
              </section>
            )}

            {studio.galleryImages.length > 0 && (
              <section aria-labelledby="gallery-heading">
                <div className="flex items-center gap-2.5 mb-4">
                  <h2
                    id="gallery-heading"
                    className="text-lg font-semibold flex items-center gap-2"
                  >
                    <Images className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                    Portfolio
                  </h2>
                  <span className="text-sm text-muted-foreground">
                    ({studio.galleryImages.length})
                  </span>
                </div>
                <div className="columns-2 sm:columns-3 gap-3">
                  {studio.galleryImages.map((url, i) => (
                    <button
                      key={url}
                      type="button"
                      onClick={() => setLightboxUrl(url)}
                      className="mb-3 break-inside-avoid block w-full group relative
                                 overflow-hidden rounded-lg cursor-zoom-in
                                 focus-visible:outline-none focus-visible:ring-2
                                 focus-visible:ring-ring focus-visible:ring-offset-1"
                      aria-label={`View portfolio image ${i + 1} of ${studio.galleryImages.length}`}
                    >
                      <img
                        src={url}
                        alt={`Tattoo portfolio work at ${studio.name} — image ${i + 1}`}
                        loading={i < 6 ? "eager" : "lazy"}
                        decoding="async"
                        className="w-full object-cover transition-transform duration-300
                                   group-hover:scale-[1.03]"
                      />
                      <div
                        className="absolute inset-0 bg-black/0 group-hover:bg-black/20
                                   transition-colors duration-200"
                      />
                    </button>
                  ))}
                </div>
              </section>
            )}

            <ReviewSection slug={studio.slug} target="studio" token={token} canRespond={canRespond} />
          </div>

          {/* Right: sticky sidebar */}
          <aside className="lg:sticky lg:top-[72px] space-y-4">

            <div className="rounded-xl border bg-muted/10 p-5 space-y-4">
              {studio.showBookingCta && (
                <Button
                  className="w-full bg-violet-600 hover:bg-violet-700
                             text-white border-0 min-h-[44px] text-sm font-semibold"
                  asChild
                >
                  <Link to={ctaUrl}>Book an Appointment</Link>
                </Button>
              )}

              {studio.phoneNumber && (
                <a
                  href={`tel:${studio.phoneNumber}`}
                  className="flex items-center gap-2 text-sm text-muted-foreground
                             hover:text-foreground transition-colors min-h-[44px]"
                  aria-label={`Call ${studio.name} at ${studio.phoneNumber}`}
                >
                  <Phone className="h-4 w-4 shrink-0" aria-hidden="true" />
                  {studio.phoneNumber}
                </a>
              )}

              {studio.instagramHandle && (
                <a
                  href={`https://instagram.com/${studio.instagramHandle.replace(/^@/, "")}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 text-sm text-muted-foreground
                             hover:text-foreground transition-colors min-h-[44px]"
                  aria-label={`${studio.name} on Instagram`}
                >
                  <AtSign className="h-4 w-4 shrink-0" aria-hidden="true" />
                  @{studio.instagramHandle.replace(/^@/, "")}
                </a>
              )}

              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
                {studio.city}
              </div>
            </div>

            <p className="text-xs text-muted-foreground/60 text-center px-1">
              Booking requests go directly to the studio.
            </p>
          </aside>
        </div>
      </div>

      <footer className="py-4 text-center text-xs text-foreground/50 border-t mt-auto">
        <a
          href="https://penaearte.com"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:text-foreground/80 hover:underline transition-colors"
        >
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
