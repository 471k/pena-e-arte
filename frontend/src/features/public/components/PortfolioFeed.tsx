import { useState } from "react";
import { Link } from "react-router-dom";
import { MapPin, X } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { StarRating } from "@/shared/components/ui/StarRating";
import {
  Dialog,
  DialogContent,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { useAppSelector } from "@/app/hooks";
import {
  useGetPortfolioFeedQuery,
  type PortfolioImageResponse,
} from "../publicApi";
import { ReviewSection } from "./ReviewSection";

// ── Props ─────────────────────────────────────────────────────────────────────

interface PortfolioFeedProps {
  lat:      number | null;
  lng:      number | null;
  radiusKm: number;
  nearOnly: boolean;
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

const SKELETON_HEIGHTS = [
  "h-52", "h-72", "h-64",
  "h-80", "h-48", "h-56",
  "h-60", "h-76", "h-44",
  "h-68", "h-52", "h-80",
] as const;

function PortfolioSkeleton() {
  return (
    <div
      className="columns-2 md:columns-3 gap-3"
      aria-label="Loading portfolio"
      aria-busy="true"
    >
      {SKELETON_HEIGHTS.map((h, i) => (
        <div key={i} className={`mb-3 break-inside-avoid ${h}`}>
          <Skeleton className="w-full h-full rounded-lg bg-muted/60 dark:bg-zinc-800" />
        </div>
      ))}
    </div>
  );
}

// ── Lightbox ──────────────────────────────────────────────────────────────────

interface LightboxProps {
  image:    PortfolioImageResponse;
  token:    string | null;
  onClose:  () => void;
}

function PortfolioLightbox({ image, token, onClose }: LightboxProps) {
  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent
        className="max-w-3xl p-0 overflow-hidden"
        aria-label={`Tattoo by ${image.artistName} at ${image.studioName}`}
      >
        <DialogTitle className="sr-only">
          Tattoo by {image.artistName} at {image.studioName}
        </DialogTitle>

        <button
          onClick={onClose}
          className="absolute right-3 top-3 z-10 rounded-full bg-black/60 p-1
                     text-white hover:bg-black/80 transition-colors"
          aria-label="Close"
        >
          <X className="h-4 w-4" />
        </button>

        <div className="grid md:grid-cols-2">
          {/* Image panel */}
          <div className="bg-black flex items-center justify-center min-h-[280px]">
            <img
              src={image.imageUrl}
              alt={`Tattoo by ${image.artistName}`}
              className="w-full h-full object-contain max-h-[70vh]"
            />
          </div>

          {/* Info + reviews panel */}
          <div className="p-5 overflow-y-auto max-h-[70vh] space-y-4">
            <div className="space-y-1">
              <Link
                to={`/artist/${image.artistSlug}`}
                className="font-semibold hover:underline"
              >
                {image.artistName}
              </Link>
              <p className="text-sm text-muted-foreground">{image.studioName}</p>

              {image.imageReviewCount > 0 && (
                <div className="flex items-center gap-1.5">
                  <StarRating value={Math.round(image.imageAverageRating ?? 0)} />
                  <span className="text-xs text-muted-foreground">
                    ({image.imageReviewCount})
                  </span>
                </div>
              )}
            </div>

            <ReviewSection
              slug={image.artistSlug}
              target="tattoo"
              token={token}
              imageId={image.imageId}
            />
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ── Individual image tile ─────────────────────────────────────────────────────

interface TileProps {
  image:   PortfolioImageResponse;
  onOpen:  (image: PortfolioImageResponse) => void;
}

function PortfolioTile({ image, onOpen }: TileProps) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;

  return (
    <button
      type="button"
      className="mb-3 break-inside-avoid block w-full relative group rounded-lg overflow-hidden
                 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
                 focus-visible:ring-offset-2 text-left"
      aria-label={`Tattoo by ${image.artistName} at ${image.studioName}`}
      onClick={() => onOpen(image)}
    >
      <img
        src={image.imageUrl}
        alt={`Tattoo by ${image.artistName}`}
        loading="lazy"
        decoding="async"
        className="w-full object-cover transition-transform duration-300
                   group-hover:scale-[1.03]"
        onError={() => setFailed(true)}
      />

      {image.distanceKm !== null && (
        <span
          className="absolute top-2 right-2
                     bg-black/60 backdrop-blur-sm
                     text-white text-[10px] font-medium
                     px-1.5 py-0.5 rounded-full
                     flex items-center gap-0.5"
        >
          <MapPin className="h-2.5 w-2.5" aria-hidden="true" />
          {image.distanceKm} km
        </span>
      )}

      <div
        className="absolute inset-0
                   bg-gradient-to-t from-black/85 via-black/25 to-transparent
                   opacity-0 group-hover:opacity-100 group-focus-visible:opacity-100
                   transition-opacity duration-200
                   flex flex-col justify-end p-3 gap-1"
      >
        <p className="text-white font-semibold text-sm leading-snug truncate">
          {image.artistName}
        </p>
        <p className="text-white/65 text-xs truncate">{image.studioName}</p>

        {image.reviewCount > 0 && (
          <div className="flex items-center gap-1.5">
            <StarRating value={Math.round(image.averageRating ?? 0)} />
            <span className="text-white/55 text-xs">({image.reviewCount})</span>
          </div>
        )}

        <span className="text-violet-300 text-xs font-medium mt-0.5">
          View tattoo →
        </span>
      </div>
    </button>
  );
}

// ── Main feed component ───────────────────────────────────────────────────────

export function PortfolioFeed({ lat, lng, radiusKm, nearOnly }: PortfolioFeedProps) {
  const [page, setPage] = useState(1);
  const [lightboxImage, setLightboxImage] = useState<PortfolioImageResponse | null>(null);

  const token = useAppSelector((s) => s.auth.token);

  const feedArgs = {
    lat:      nearOnly && lat != null ? lat : undefined,
    lng:      nearOnly && lng != null ? lng : undefined,
    radiusKm: nearOnly ? radiusKm : 50,
    page,
  };

  const { data: images, isLoading, isFetching } =
    useGetPortfolioFeedQuery(feedArgs);

  if (isLoading) return <PortfolioSkeleton />;

  if (!images || images.length === 0) {
    return (
      <div className="flex flex-col items-center gap-4 py-24 text-center">
        <div className="rounded-full bg-muted/40 p-6">
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            className="h-9 w-9 stroke-current fill-none stroke-[1.5] text-muted-foreground/40"
          >
            <path strokeLinecap="round" strokeLinejoin="round"
              d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828L11.828
                 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0 019
                 10.172V11z" />
          </svg>
        </div>
        <div className="space-y-1.5">
          <p className="text-base font-semibold">No portfolio work yet</p>
          <p className="text-sm text-muted-foreground max-w-xs">
            {nearOnly
              ? "No artists with portfolio images found nearby. Try a larger radius or turn off the location filter."
              : "Be among the first artists to show your work here."}
          </p>
        </div>
        <Link
          to="/register"
          className="text-sm text-violet-400 hover:text-violet-300 underline
                     underline-offset-4 transition-colors"
        >
          Register your studio →
        </Link>
      </div>
    );
  }

  const hasMore = images.length >= page * 24;

  return (
    <div className="space-y-6">
      <div className="columns-2 md:columns-3 gap-3">
        {images.map((img) => (
          <PortfolioTile
            key={img.imageId}
            image={img}
            onOpen={setLightboxImage}
          />
        ))}
      </div>

      {hasMore && (
        <div className="flex justify-center pt-2 pb-6">
          <Button
            variant="outline"
            onClick={() => setPage((p) => p + 1)}
            disabled={isFetching}
            className="min-w-[140px]"
          >
            {isFetching ? "Loading…" : "Load more"}
          </Button>
        </div>
      )}

      {lightboxImage !== null && (
        <PortfolioLightbox
          image={lightboxImage}
          token={token}
          onClose={() => setLightboxImage(null)}
        />
      )}
    </div>
  );
}
