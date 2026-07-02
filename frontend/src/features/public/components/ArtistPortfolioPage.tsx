import { useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ChevronLeft,
  Images,
  X,
  ZoomIn,
} from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { StarRating }  from "@/shared/components/ui/StarRating";
import { useAppSelector } from "@/app/hooks";
import { useGetPublicArtistQuery, useRecordArtistViewMutation, type ArtistPortfolioImage } from "../publicApi";
import { useDocumentMeta }         from "@/shared/utils/useDocumentMeta";
import { useStructuredData }       from "@/shared/utils/useStructuredData";
import { ReviewSection }           from "./ReviewSection";
import { useEffect } from "react";

// ── Document meta ──────────────────────────────────────────────────────────────

function ArtistMeta({
  name, slug, bio, coverImage,
}: {
  name: string; slug: string; bio: string | null; coverImage?: string;
}) {
  useDocumentMeta({
    title:       `${name} — Tattoo Artist on Pena e Artë`,
    description: bio ?? `View the tattoo portfolio of ${name}.`,
    ogImage:     coverImage,
    canonical:   `https://penaearte.com/artist/${slug}`,
  });
  useStructuredData({
    "@context":  "https://schema.org",
    "@type":     "Person",
    jobTitle:    "Tattoo Artist",
    name,
    description: bio ?? undefined,
    image:       coverImage,
    url:         `https://penaearte.com/artist/${slug}`,
  });
  return null;
}

// ── Avatar ─────────────────────────────────────────────────────────────────────

function ArtistAvatar({
  name, profileImageUrl,
}: {
  name: string; profileImageUrl: string | null;
}) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");

  if (profileImageUrl) {
    return (
      <img
        src={profileImageUrl}
        alt={`Profile photo of ${name}`}
        className="h-24 w-24 rounded-full object-cover ring-2 ring-border/60"
      />
    );
  }

  return (
    <div
      className="h-24 w-24 rounded-full bg-gradient-to-br from-zinc-700 to-zinc-800
                 ring-2 ring-border/40 flex items-center justify-center"
      aria-hidden="true"
    >
      <span className="text-2xl font-bold text-white/25 select-none">{initials}</span>
    </div>
  );
}

// ── Specialization chips ───────────────────────────────────────────────────────

function SpecializationChips({ value }: { value: string }) {
  const tags = value.split(",").map((s) => s.trim()).filter(Boolean);
  if (tags.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5">
      {tags.map((tag) => (
        <span
          key={tag}
          className="text-xs px-2.5 py-1 rounded-full
                     bg-muted/60 text-muted-foreground/90
                     border border-border/50"
        >
          {tag}
        </span>
      ))}
    </div>
  );
}

// ── Rating summary ─────────────────────────────────────────────────────────────

function RatingSummary({
  averageRating, reviewCount, onWriteReview,
}: {
  averageRating: number | null;
  reviewCount:   number;
  onWriteReview: () => void;
}) {
  if (reviewCount === 0) {
    return (
      <div className="flex items-center gap-2">
        <StarRating value={0} />
        <button
          type="button"
          onClick={onWriteReview}
          className="text-xs text-violet-400 hover:text-violet-300 transition-colors
                     underline underline-offset-2"
        >
          Be the first to review
        </button>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-2 flex-wrap">
      <StarRating value={Math.round(averageRating ?? 0)} />
      <span className="text-sm text-muted-foreground">
        {averageRating?.toFixed(1)} · {reviewCount} review{reviewCount !== 1 ? "s" : ""}
      </span>
      <button
        type="button"
        onClick={onWriteReview}
        className="text-xs text-violet-400 hover:text-violet-300 transition-colors
                   underline underline-offset-2 ml-auto"
      >
        Leave a review
      </button>
    </div>
  );
}

// ── Profile Strength nudge (own profile only) ──────────────────────────────────

function ProfileStrengthNudge({
  hasBio, hasAvatar, hasSpecializations, hasPortfolio, hasRate,
}: {
  hasBio:             boolean;
  hasAvatar:          boolean;
  hasSpecializations: boolean;
  hasPortfolio:       boolean;
  hasRate:            boolean;
}) {
  const items = [hasBio, hasAvatar, hasSpecializations, hasPortfolio, hasRate];
  const done  = items.filter(Boolean).length;
  const total = items.length;
  const pct   = Math.round((done / total) * 100);

  if (pct === 100) return null;

  const missing: string[] = [];
  if (!hasBio)             missing.push("Add a bio");
  if (!hasAvatar)          missing.push("Upload a profile photo");
  if (!hasSpecializations) missing.push("Add your specialties");
  if (!hasPortfolio)       missing.push("Upload at least 1 portfolio image");
  if (!hasRate)            missing.push("Set your hourly rate");

  return (
    <div className="rounded-lg border border-amber-800/50 bg-amber-950/20 p-4 space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-amber-300">
          Profile {pct}% complete
        </p>
        <span className="text-xs text-amber-400/70">{done}/{total} sections</span>
      </div>

      <div className="h-1.5 rounded-full bg-amber-900/40 overflow-hidden">
        <div
          className="h-full rounded-full bg-amber-400 transition-all duration-500"
          style={{ width: `${pct}%` }}
          aria-valuenow={pct}
          aria-valuemin={0}
          aria-valuemax={100}
          role="progressbar"
          aria-label="Profile completion"
        />
      </div>

      <ul className="space-y-1">
        {missing.map((action) => (
          <li key={action} className="flex items-center gap-1.5 text-xs text-amber-300/70">
            <span aria-hidden="true">·</span>
            {action}
          </li>
        ))}
      </ul>

      <p className="text-xs text-amber-400/60">
        Only visible to you. Complete your profile to attract more clients.
      </p>
    </div>
  );
}

// ── Portfolio masonry ──────────────────────────────────────────────────────────

function PortfolioGrid({
  images, artistName, onImageClick,
}: {
  images:       ArtistPortfolioImage[];
  artistName:   string;
  onImageClick: (item: ArtistPortfolioImage) => void;
}) {
  if (images.length === 0) {
    return (
      <div className="flex flex-col items-center gap-3 py-16 text-center rounded-lg
                      border border-dashed border-border/50">
        <Images className="h-8 w-8 text-muted-foreground/30" aria-hidden="true" />
        <p className="text-sm text-muted-foreground">No portfolio images yet.</p>
      </div>
    );
  }

  return (
    <div className="columns-2 sm:columns-3 gap-3">
      {images.map((item, i) => (
        <button
          key={item.imageId}
          type="button"
          onClick={() => onImageClick(item)}
          className="mb-3 break-inside-avoid block w-full group relative
                     overflow-hidden rounded-lg cursor-zoom-in
                     focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
                     focus-visible:ring-offset-1"
          aria-label={`View portfolio image ${i + 1} of ${images.length} by ${artistName}`}
        >
          <img
            src={item.imageUrl}
            alt={`Tattoo by ${artistName} — image ${i + 1}`}
            loading={i < 6 ? "eager" : "lazy"}
            decoding="async"
            className="w-full object-cover transition-transform duration-300
                       group-hover:scale-[1.03]"
          />
          <div
            className="absolute inset-0 bg-black/0 group-hover:bg-black/25
                       transition-colors duration-200
                       flex items-center justify-center"
          >
            <ZoomIn
              className="h-6 w-6 text-white drop-shadow-lg
                         opacity-0 group-hover:opacity-100 transition-opacity duration-200"
              aria-hidden="true"
            />
          </div>
        </button>
      ))}
    </div>
  );
}

// ── Lightbox ───────────────────────────────────────────────────────────────────

interface LightboxItem {
  imageId:  string;
  imageUrl: string;
}

function Lightbox({
  item, artistName, artistSlug, token, onClose,
}: {
  item:       LightboxItem | null;
  artistName: string;
  artistSlug: string;
  token:      string | null;
  onClose:    () => void;
}) {
  return (
    <Dialog open={!!item} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent
        className="max-w-3xl w-full p-0 overflow-hidden"
        aria-label={`Portfolio image by ${artistName}`}
        aria-describedby={undefined}
      >
        {item && (
          <div className="grid md:grid-cols-2">
            <DialogTitle className="sr-only">Portfolio image by {artistName}</DialogTitle>

            <div className="bg-black flex items-center justify-center min-h-[240px]">
              <img
                src={item.imageUrl}
                alt={`Tattoo portfolio by ${artistName}`}
                className="w-full h-auto max-h-[70vh] object-contain"
              />
            </div>

            <div className="p-5 overflow-y-auto max-h-[70vh] space-y-4">
              <button
                type="button"
                onClick={onClose}
                aria-label="Close image"
                className="absolute top-3 right-3 rounded-full bg-black/60 backdrop-blur-sm
                           p-1.5 text-white hover:bg-black/80 transition-colors"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>

              <ReviewSection
                slug={artistSlug}
                target="tattoo"
                token={token}
                imageId={item.imageId}
              />
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Skeleton ───────────────────────────────────────────────────────────────────

function ArtistPageSkeleton() {
  return (
    <div
      className="max-w-6xl mx-auto px-4 py-8"
      aria-label="Loading artist profile"
      aria-busy="true"
    >
      <Skeleton className="h-4 w-32 mb-6" />

      <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-8 lg:gap-12">
        <div className="space-y-5">
          <Skeleton className="h-24 w-24 rounded-full" />
          <Skeleton className="h-7 w-48" />
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-16 w-full" />
          <div className="flex gap-1.5">
            <Skeleton className="h-6 w-20 rounded-full" />
            <Skeleton className="h-6 w-24 rounded-full" />
          </div>
          <Skeleton className="h-11 w-full rounded-md" />
        </div>

        <div className="columns-2 sm:columns-3 gap-3">
          {["h-52", "h-40", "h-64", "h-48", "h-60", "h-44"].map((h, i) => (
            <div key={i} className={`mb-3 break-inside-avoid ${h}`}>
              <Skeleton className="w-full h-full rounded-lg" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────

export function ArtistPortfolioPage() {
  const { slug = "" }  = useParams<{ slug: string }>();
  const token          = useAppSelector((s) => s.auth.token);
  const reviewsRef     = useRef<HTMLDivElement>(null);
  const [lightboxItem, setLightboxItem] = useState<LightboxItem | null>(null);

  const { data: artist, isLoading, isError } =
    useGetPublicArtistQuery(slug, { skip: !slug });

  const [recordView] = useRecordArtistViewMutation();

  // Track portfolio view for discovery feed ranking.
  useEffect(() => {
    if (!slug) return;
    void recordView(slug);
  }, [slug, recordView]);

  function scrollToReviews() {
    reviewsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  if (isLoading) return <ArtistPageSkeleton />;

  if (isError || !artist) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Artist not found.</p>
        <Button variant="outline" asChild>
          <Link to="/discover">Browse artists</Link>
        </Button>
      </div>
    );
  }

  const bookUrl = `/book?studio=${artist.studioSlug}&artist=${artist.slug}`;
  const ctaUrl  = token ? bookUrl : `/login?redirect=${encodeURIComponent(bookUrl)}`;

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <ArtistMeta
        name={artist.name}
        slug={artist.slug}
        bio={artist.bio}
        coverImage={artist.portfolioImages[0]?.imageUrl ?? artist.profileImageUrl ?? undefined}
      />

      <Lightbox
        item={lightboxItem}
        artistName={artist.name}
        artistSlug={artist.slug}
        token={token}
        onClose={() => setLightboxItem(null)}
      />

      <div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8 space-y-6">
        {/* Back link */}
        <Link
          to={`/s/${artist.studioSlug}`}
          className="inline-flex items-center gap-1 text-sm text-muted-foreground
                     hover:text-foreground transition-colors
                     py-2 -my-2 min-h-[44px]"
          aria-label={`Back to ${artist.studioName}`}
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          {artist.studioName}
        </Link>

        {/* Two-column layout */}
        <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-8 lg:gap-12 items-start">

          {/* LEFT: sticky profile panel */}
          <aside className="lg:sticky lg:top-6 space-y-5">

            {artist.isOwnProfile && (
              <ProfileStrengthNudge
                hasBio={!!artist.bio}
                hasAvatar={!!artist.profileImageUrl}
                hasSpecializations={!!artist.specializations}
                hasPortfolio={artist.portfolioImages.length > 0}
                hasRate={artist.hourlyRate != null}
              />
            )}

            <ArtistAvatar
              name={artist.name}
              profileImageUrl={artist.profileImageUrl}
            />

            <div className="space-y-1.5">
              <h1 className="text-2xl font-bold tracking-tight">{artist.name}</h1>
              <p className="text-sm text-muted-foreground">
                at{" "}
                <Link
                  to={`/s/${artist.studioSlug}`}
                  className="hover:text-foreground underline underline-offset-2 transition-colors"
                >
                  {artist.studioName}
                </Link>
              </p>
            </div>

            <RatingSummary
              averageRating={artist.averageRating}
              reviewCount={artist.reviewCount}
              onWriteReview={scrollToReviews}
            />

            {artist.bio && (
              <p className="text-sm text-muted-foreground/90 leading-relaxed whitespace-pre-wrap">
                {artist.bio}
              </p>
            )}

            {artist.specializations && (
              <SpecializationChips value={artist.specializations} />
            )}

            {artist.hourlyRate != null && (
              <p className="text-sm text-muted-foreground">
                From{" "}
                <span className="font-semibold text-foreground">
                  €{artist.hourlyRate}/hr
                </span>
              </p>
            )}

            {artist.showBookingCta && (
              <Button
                className="w-full bg-violet-600 hover:bg-violet-700
                           text-white border-0 min-h-[44px] text-sm font-semibold"
                asChild
              >
                <Link to={ctaUrl}>
                  Book an Appointment
                </Link>
              </Button>
            )}

            <div className="rounded-lg border bg-muted/20 p-3.5 space-y-1">
              <p className="text-xs text-muted-foreground uppercase tracking-wide font-medium">
                Studio
              </p>
              <Link
                to={`/s/${artist.studioSlug}`}
                className="text-sm font-medium hover:underline underline-offset-2"
              >
                {artist.studioName}
              </Link>
            </div>
          </aside>

          {/* RIGHT: portfolio + reviews */}
          <div className="space-y-12">

            <section aria-labelledby="portfolio-heading">
              <div className="flex items-center gap-2.5 mb-5">
                <h2 id="portfolio-heading" className="text-lg font-semibold flex items-center gap-2">
                  <Images className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                  Portfolio
                </h2>
                {artist.portfolioImages.length > 0 && (
                  <span className="text-sm text-muted-foreground">
                    ({artist.portfolioImages.length}{" "}
                    {artist.portfolioImages.length === 1 ? "image" : "images"})
                  </span>
                )}
              </div>

              <PortfolioGrid
                images={artist.portfolioImages}
                artistName={artist.name}
                onImageClick={(item) => setLightboxItem(item)}
              />
            </section>

            <div ref={reviewsRef}>
              <ReviewSection slug={artist.slug} target="artist" token={token} />
            </div>
          </div>
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
