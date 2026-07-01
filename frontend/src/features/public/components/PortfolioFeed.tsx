import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Bookmark, MapPin, X } from "lucide-react";
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
  type PortfolioFeedArgs,
} from "../publicApi";
import {
  useGetSavedImageIdsQuery,
  useSaveImageMutation,
  useUnsaveImageMutation,
} from "../savedImagesApi";
import { ReviewSection } from "./ReviewSection";

// ── Props ─────────────────────────────────────────────────────────────────────

interface PortfolioFeedProps {
  lat:      number | null;
  lng:      number | null;
  radiusKm: number;
  nearOnly: boolean;
}

// ── Style chips ───────────────────────────────────────────────────────────────

// Keep in sync with TattooStyle.cs constants on the backend.
const STYLES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "",               label: "All"             },
  { value: "blackwork",      label: "Blackwork"       },
  { value: "realism",        label: "Realism"         },
  { value: "traditional",    label: "Traditional"     },
  { value: "geometric",      label: "Geometric"       },
  { value: "fineline",       label: "Fineline"        },
  { value: "watercolor",     label: "Watercolor"      },
  { value: "neo-traditional", label: "Neo-Traditional" },
  { value: "japanese",       label: "Japanese"        },
];

interface StyleChipsProps {
  activeStyle: string;
  onChange:    (style: string) => void;
}

function StyleChips({ activeStyle, onChange }: StyleChipsProps) {
  return (
    <div
      role="group"
      aria-label="Filter by tattoo style"
      className="flex items-center gap-1.5 overflow-x-auto scrollbar-none pb-1
                 -mx-4 px-4 sm:mx-0 sm:px-0"
    >
      {STYLES.map(({ value, label }) => {
        const isActive = activeStyle === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={isActive}
            onClick={() => onChange(value)}
            className={`shrink-0 px-3 py-1 rounded-full text-xs font-medium
                        border transition-colors whitespace-nowrap
                        ${isActive
                          ? "bg-violet-600 border-violet-500 text-white"
                          : "border-border text-muted-foreground hover:text-foreground hover:border-border/80"
                        }`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
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
      className="flex gap-3"
      aria-label="Loading portfolio"
      aria-busy="true"
    >
      {[0, 1, 2].map((col) => (
        <div key={col} className="flex flex-col gap-3 flex-1 min-w-0">
          {SKELETON_HEIGHTS.slice(col * 4, col * 4 + 4).map((h, i) => (
            <Skeleton key={i} className={`w-full ${h} rounded-lg bg-muted/60 dark:bg-zinc-800`} />
          ))}
        </div>
      ))}
    </div>
  );
}

// ── Column distribution ───────────────────────────────────────────────────────

function useColumnCount(): 1 | 2 | 3 {
  const getCount = (): 1 | 2 | 3 => {
    if (typeof window === "undefined") return 2;
    if (window.innerWidth >= 1024) return 3;
    if (window.innerWidth >= 640)  return 2;
    return 1;
  };

  const [count, setCount] = useState<1 | 2 | 3>(getCount);

  // Acceptable useEffect: ResizeObserver/resize event — browser API side-effect.
  useEffect(() => {
    const update = () => setCount(getCount());
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  return count;
}

function distributeToColumns<T>(items: T[], columnCount: number): T[][] {
  const cols: T[][] = Array.from({ length: columnCount }, () => []);
  items.forEach((item, i) => cols[i % columnCount].push(item));
  return cols;
}

// ── Lightbox ──────────────────────────────────────────────────────────────────

interface LightboxProps {
  image:   PortfolioImageResponse;
  token:   string | null;
  onClose: () => void;
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

              {image.style && (
                <span className="inline-block text-[10px] font-medium uppercase tracking-wider
                                 px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-300 border border-zinc-700">
                  {image.style}
                </span>
              )}

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
  image:        PortfolioImageResponse;
  isSaved:      boolean;
  onOpen:       (image: PortfolioImageResponse) => void;
  onToggleSave: (imageId: string, isSaved: boolean) => void;
  showBookmark: boolean;
}

function PortfolioTile({ image, isSaved, onOpen, onToggleSave, showBookmark }: TileProps) {
  const [failed, setFailed] = useState(false);

  if (failed) {
    return (
      <div
        role="listitem"
        className="h-40 rounded-lg
                   bg-muted/40 border border-border/30
                   flex flex-col items-center justify-center gap-1 text-center px-3"
        aria-label={`Image unavailable — ${image.artistName}`}
      >
        <p className="text-xs text-muted-foreground/70">Image unavailable</p>
        <p className="text-[10px] text-muted-foreground/50">{image.artistName}</p>
      </div>
    );
  }

  return (
    <div
      role="listitem"
      className="relative rounded-lg overflow-hidden group
                 focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2"
    >
      {/* Main clickable image */}
      <button
        type="button"
        className="block w-full text-left focus-visible:outline-none cursor-pointer"
        aria-label={`View tattoo by ${image.artistName} at ${image.studioName}`}
        onClick={() => onOpen(image)}
      >
        <img
          src={image.imageUrl}
          alt={`Tattoo by ${image.artistName}`}
          loading="lazy"
          decoding="async"
          className="w-full object-cover block transition-transform duration-300
                     group-hover:scale-[1.02]"
          onError={() => setFailed(true)}
        />

        {/* Hover overlay — extra context and CTA */}
        <div
          aria-hidden="true"
          className="absolute inset-0
                     bg-gradient-to-t from-black/80 via-black/20 to-transparent
                     opacity-0 group-hover:opacity-100 group-focus-within:opacity-100
                     transition-opacity duration-200
                     flex flex-col justify-end px-3 pb-10 gap-0.5"
        >
          {image.imageReviewCount > 0 && (
            <div className="flex items-center gap-1">
              <StarRating value={Math.round(image.imageAverageRating ?? 0)} />
              <span className="text-white/60 text-[10px]">({image.imageReviewCount})</span>
            </div>
          )}
          <span className="text-violet-300 text-xs font-medium">View tattoo →</span>
        </div>
      </button>

      {/* Distance badge (top-right) */}
      {image.distanceKm !== null && (
        <span
          aria-label={`${image.distanceKm} km away`}
          className="absolute top-2 right-2 z-10
                     bg-black/70 backdrop-blur-sm text-white text-[10px] font-medium
                     px-1.5 py-0.5 rounded-full flex items-center gap-0.5"
        >
          <MapPin className="h-2.5 w-2.5" aria-hidden="true" />
          {image.distanceKm} km
        </span>
      )}

      {/* Bookmark button (top-left) — only when authenticated */}
      {showBookmark && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onToggleSave(image.imageId, isSaved); }}
          aria-label={isSaved
            ? `Remove ${image.artistName}'s tattoo from saved`
            : `Save ${image.artistName}'s tattoo`}
          aria-pressed={isSaved}
          className={`absolute top-2 left-2 z-10 p-1.5 rounded-full
                      backdrop-blur-sm
                      ${isSaved
                        ? "bg-violet-600 text-white"
                        : "bg-black/60 text-white/70 hover:text-white hover:bg-black/80"
                      }
                      opacity-0 group-hover:opacity-100 group-focus-within:opacity-100
                      focus-visible:opacity-100
                      transition-all duration-200`}
        >
          <Bookmark className={`h-3.5 w-3.5 ${isSaved ? "fill-current" : ""}`} aria-hidden="true" />
        </button>
      )}

      {/* Always-visible attribution strip */}
      <div className="bg-zinc-950/85 backdrop-blur-sm px-2.5 py-1.5 flex items-center gap-2">
        <div className="flex-1 min-w-0">
          <p className="text-white text-[11px] font-medium truncate leading-tight">
            {image.artistName}
          </p>
          <p className="text-white/50 text-[10px] truncate leading-tight">
            {image.studioName}
          </p>
        </div>
        {image.reviewCount > 0 && (
          <div className="flex items-center gap-0.5 shrink-0">
            <span className="text-yellow-400 text-[10px]">★</span>
            <span className="text-white/60 text-[10px]">
              {image.averageRating?.toFixed(1)}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}

// ── Masonry grid ──────────────────────────────────────────────────────────────

interface MasonryGridProps {
  images:       PortfolioImageResponse[];
  onOpen:       (image: PortfolioImageResponse) => void;
  savedIds:     ReadonlySet<string>;
  onToggleSave: (imageId: string, isSaved: boolean) => void;
  token:        string | null;
}

function MasonryGrid({ images, onOpen, savedIds, onToggleSave, token }: MasonryGridProps) {
  const columnCount = useColumnCount();
  const columns     = distributeToColumns(images, columnCount);

  return (
    <div className="flex gap-3" role="list" aria-label="Portfolio images">
      {columns.map((col, colIdx) => (
        <div key={colIdx} className="flex flex-col gap-3 flex-1 min-w-0">
          {col.map((img) => (
            <PortfolioTile
              key={img.imageId}
              image={img}
              isSaved={savedIds.has(img.imageId)}
              onOpen={onOpen}
              onToggleSave={onToggleSave}
              showBookmark={token !== null}
            />
          ))}
        </div>
      ))}
    </div>
  );
}

// ── Main feed component ───────────────────────────────────────────────────────

export function PortfolioFeed({ lat, lng, radiusKm, nearOnly }: PortfolioFeedProps) {
  const [page,          setPage]        = useState(1);
  const [activeStyle,   setActiveStyle] = useState("");
  const [lightboxImage, setLightboxImage] = useState<PortfolioImageResponse | null>(null);
  const [allImages,     setAllImages]   = useState<PortfolioImageResponse[]>([]);

  const token  = useAppSelector((s) => s.auth.token);
  const userId = useAppSelector((s) => s.auth.user?.id);

  const feedArgs: PortfolioFeedArgs = {
    lat:      nearOnly && lat != null ? lat : undefined,
    lng:      nearOnly && lng != null ? lng : undefined,
    radiusKm: nearOnly ? radiusKm : 50,
    page,
    style:    activeStyle || undefined,
  };

  const { data: images, isLoading, isFetching, isError } = useGetPortfolioFeedQuery(feedArgs);

  // Saved image IDs — only fetch when logged in
  const { data: savedIds = [] } = useGetSavedImageIdsQuery(undefined, { skip: !token });
  const [saveImage]   = useSaveImageMutation();
  const [unsaveImage] = useUnsaveImageMutation();

  const savedSet = useMemo(() => new Set(savedIds), [savedIds]);

  // Accumulate pages (infinite scroll append).
  // This useEffect accumulates API results into local state — it does NOT fetch.
  // RTK Query does the fetching; this effect only merges pages into the list.
  useEffect(() => {
    if (images) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setAllImages((prev) => page === 1 ? images : [...prev, ...images]);
    }
  }, [images, page]);

  // Reset when nearOnly/location props change — external props driving local pagination state.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(1);
    setAllImages([]);
  }, [nearOnly, lat, lng]);

  function handleStyleChange(style: string) {
    setActiveStyle(style);
    setPage(1);
    setAllImages([]);
  }

  function handleToggleSave(imageId: string, isSaved: boolean) {
    if (!userId) return;
    if (isSaved) void unsaveImage(imageId);
    else void saveImage(imageId);
  }

  if (isLoading && page === 1) return (
    <div className="space-y-4">
      <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />
      <PortfolioSkeleton />
    </div>
  );

  if (isError && allImages.length === 0) {
    return (
      <div className="space-y-4">
        <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />
        <div className="flex flex-col items-center gap-4 py-24 text-center">
          <div className="rounded-full bg-destructive/10 p-6">
            <svg
              aria-hidden="true"
              viewBox="0 0 24 24"
              className="h-9 w-9 stroke-current fill-none stroke-[1.5] text-destructive/60"
            >
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="8" x2="12" y2="12" strokeLinecap="round" />
              <circle cx="12" cy="16" r="0.5" fill="currentColor" />
            </svg>
          </div>
          <div className="space-y-1.5">
            <p className="text-base font-semibold">Could not load portfolio</p>
            <p className="text-sm text-muted-foreground max-w-xs">
              The server returned an error. Please try again in a moment.
            </p>
          </div>
          <button
            type="button"
            onClick={() => { setPage(1); setAllImages([]); }}
            className="text-sm text-violet-400 hover:text-violet-300 underline
                       underline-offset-4 transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  if (allImages.length === 0 && !isLoading) {
    return (
      <div className="space-y-4">
        <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />
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
                : activeStyle
                  ? `No ${activeStyle} tattoos found. Try a different style or browse all.`
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
      </div>
    );
  }

  const hasMore = (images?.length ?? 0) >= 24;

  return (
    <div className="space-y-4">
      <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />

      <MasonryGrid
        images={allImages}
        onOpen={setLightboxImage}
        savedIds={savedSet}
        onToggleSave={handleToggleSave}
        token={token}
      />

      {hasMore && (
        <div className="flex justify-center pt-2 pb-6">
          <Button
            variant="outline"
            onClick={() => setPage((p) => p + 1)}
            disabled={isFetching}
            aria-label={isFetching ? "Loading more images" : "Load more portfolio images"}
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
