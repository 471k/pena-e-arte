import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Locate, MapPin, Search, Users } from "lucide-react";
import { Button }         from "@/shared/components/ui/button";
import { Skeleton }       from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { StarRating }     from "@/shared/components/ui/StarRating";
import { PortfolioFeed }  from "./PortfolioFeed";
import {
  useGetNearbyStudiosQuery,
  type NearbyStudioResponse,
} from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

// ── Constants ─────────────────────────────────────────────────────────────────

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

const DEFAULT_LAT  = 38.7169;
const DEFAULT_LNG  = -9.1395;
const DEFAULT_CITY = "Lisbon, Portugal";

type ActiveTab = "portfolio" | "studios";

// ── Types ─────────────────────────────────────────────────────────────────────

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function DistanceLabel({ km }: { km: number }) {
  if (km < 1) {
    return (
      <span>
        {Math.round(km * 1000)}{" "}
        <span className="text-[10px] opacity-60">m</span>
      </span>
    );
  }
  return (
    <span>
      {km}{" "}
      <span className="text-[10px] opacity-60">km</span>
    </span>
  );
}

function StudioMonogram({ name }: { name: string }) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
  return (
    <div
      className="h-40 rounded-t-lg flex items-center justify-center
                 bg-gradient-to-br from-zinc-800 to-zinc-900"
      aria-hidden="true"
    >
      <span className="text-3xl font-bold text-white/20 select-none">{initials}</span>
    </div>
  );
}

// ── StudioCard ────────────────────────────────────────────────────────────────

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    <Link
      to={`/s/${studio.slug}`}
      className="block rounded-lg focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
    >
      <Card className="hover:border-border/80 hover:shadow-md hover:shadow-black/20
                       transition-all cursor-pointer h-full">
        {studio.coverImageUrl ? (
          <div className="h-40 bg-muted overflow-hidden rounded-t-lg">
            <img
              src={studio.coverImageUrl}
              alt={`${studio.name} cover`}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          </div>
        ) : (
          <StudioMonogram name={studio.name} />
        )}
        <CardContent className="p-4 space-y-2">
          <p className="font-semibold text-sm leading-tight line-clamp-1">{studio.name}</p>

          <div className="flex items-center gap-1.5">
            {studio.reviewCount > 0 ? (
              <>
                <StarRating value={Math.round(studio.averageRating ?? 0)} />
                <span className="text-xs text-muted-foreground">({studio.reviewCount})</span>
              </>
            ) : (
              <span className="text-xs text-muted-foreground/60 italic">No reviews yet</span>
            )}
          </div>

          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="truncate">{studio.city}</span>
            <span className="ml-auto font-medium text-foreground/80 whitespace-nowrap">
              <DistanceLabel km={studio.distanceKm} />
            </span>
          </div>

          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" aria-hidden="true" />
              <span>{studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}</span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

// ── Studio grid skeleton ───────────────────────────────────────────────────────

function StudioSkeleton() {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
         aria-label="Loading studios" aria-busy="true">
      {Array.from({ length: 6 }).map((_, i) => (
        <div key={i} className="rounded-lg overflow-hidden border border-border/40">
          <Skeleton className="h-40 w-full rounded-none" />
          <div className="p-4 space-y-2.5">
            <Skeleton className="h-4 w-36" />
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-3 w-20" />
          </div>
        </div>
      ))}
    </div>
  );
}

// ── Meta ──────────────────────────────────────────────────────────────────────

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Discover Tattoo Art Near You — Pena e Artë",
    description: "Browse tattoo portfolios and studios near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function DiscoverPage() {
  const hasGeo = "geolocation" in navigator;

  const [lat,           setLat]           = useState<number | null>(hasGeo ? null : DEFAULT_LAT);
  const [lng,           setLng]           = useState<number | null>(hasGeo ? null : DEFAULT_LNG);
  const [locationName,  setLocationName]  = useState<string>(hasGeo ? "" : DEFAULT_CITY);
  const [isGeoLocating, setIsGeoLocating] = useState<boolean>(hasGeo);
  const [radiusKm,      setRadiusKm]      = useState<Radius>(50);
  const [searchInput,   setSearchInput]   = useState<string>("");
  const [searchError,   setSearchError]   = useState<string | null>(null);
  const [isGeocoding,   setIsGeocoding]   = useState(false);
  const [activeTab,     setActiveTab]     = useState<ActiveTab>("portfolio");
  // nearOnly: when true, portfolio feed is filtered to the user's radius
  const [nearOnly,      setNearOnly]      = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);

  // ── Reverse geocode ───────────────────────────────────────────────────────
  const reverseGeocode = useCallback(async (latitude: number, longitude: number) => {
    try {
      const res  = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=json&lat=${latitude}&lon=${longitude}`,
        { headers: { "Accept-Language": "en" } },
      );
      const data = (await res.json()) as {
        address?: { city?: string; town?: string; village?: string; country?: string };
      };
      const place   = data.address?.city ?? data.address?.town ?? data.address?.village ?? "";
      const country = data.address?.country ?? "";
      setLocationName(place && country ? `${place}, ${country}` : place || DEFAULT_CITY);
    } catch {
      setLocationName(DEFAULT_CITY);
    }
  }, []);

  // ── Geolocation on mount ──────────────────────────────────────────────────
  // Acceptable useEffect: browser API side-effect, not data fetching.
  useEffect(() => {
    if (!hasGeo) return;
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setIsGeoLocating(false);
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 60_000 },
    );
  }, [hasGeo, reverseGeocode]);

  // ── Re-trigger geolocation ────────────────────────────────────────────────
  function handleUseMyLocation() {
    if (!hasGeo) return;
    setIsGeoLocating(true);
    setLocationName("");
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setIsGeoLocating(false);
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 0 },
    );
  }

  // ── Forward geocode ───────────────────────────────────────────────────────
  async function handleLocationSearch() {
    const q = searchInput.trim();
    if (!q) return;
    setIsGeocoding(true);
    setSearchError(null);
    try {
      const res     = await fetch(
        `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(q)}&limit=1`,
        { headers: { "Accept-Language": "en" } },
      );
      const results = (await res.json()) as NominatimResult[];
      if (results.length === 0) {
        setSearchError("Location not found. Try a different city name.");
        return;
      }
      const [first] = results;
      setLat(parseFloat(first.lat));
      setLng(parseFloat(first.lon));
      setLocationName(first.display_name.split(",").slice(0, 2).join(", ").trim());
      setSearchInput("");
      inputRef.current?.blur();
    } catch {
      setSearchError("Could not reach location service. Try again.");
    } finally {
      setIsGeocoding(false);
    }
  }

  // ── Studios query (only runs on Studios tab) ──────────────────────────────
  const { data: studios, isLoading: isStudiosLoading, isFetching: isStudiosFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: activeTab !== "studios" || lat === null || lng === null },
    );

  const isLoadingStudios = lat === null || isStudiosLoading || isStudiosFetching;

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <DiscoverMeta />

      {/* ── Sticky header ───────────────────────────────────────────────── */}
      <header className="sticky top-0 z-[100] border-b bg-background/95 backdrop-blur-sm">
        {/* Top row: brand + nav */}
        <div className="flex items-center justify-between px-4 py-2.5">
          <div className="flex items-center gap-2">
            <svg aria-hidden="true" viewBox="0 0 24 24"
                 className="h-5 w-5 fill-none stroke-current stroke-2">
              <path strokeLinecap="round" strokeLinejoin="round"
                d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828
                   L11.828 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0
                   019 10.172V11z" />
            </svg>
            <span className="font-semibold tracking-tight text-sm">Pena e Artë</span>
          </div>

          <nav className="flex items-center gap-1" aria-label="Site navigation">
            <Link to="/map"
              className="text-xs text-muted-foreground hover:text-foreground
                         transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
              View on map
            </Link>
            <Link to="/login"
              className="text-xs text-muted-foreground hover:text-foreground
                         transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
              Sign in
            </Link>
            <Link to="/register"
              className="text-xs font-medium px-3 py-2 rounded-md border
                         border-violet-500/60 text-violet-400
                         hover:bg-violet-500/10 hover:border-violet-400 transition-colors">
              Register studio
            </Link>
          </nav>
        </div>

        {/* Bottom row: search + tabs + location toggle */}
        <div className="flex items-center gap-2 px-4 pb-2.5">
          {/* Search input */}
          <div className="flex flex-1 items-center gap-2 max-w-sm">
            <div className="relative flex-1">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                                 text-muted-foreground pointer-events-none" aria-hidden="true" />
              <input
                ref={inputRef}
                type="search"
                placeholder="Search city…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
                aria-label="Search for a city"
                className="w-full h-9 pl-8 pr-3 rounded-md border bg-background text-xs
                           focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-1
                           placeholder:text-muted-foreground"
              />
            </div>
            <Button
              size="sm"
              onClick={() => void handleLocationSearch()}
              disabled={isGeocoding || !searchInput.trim()}
              aria-label="Search"
              className="h-9 px-3 bg-violet-600 hover:bg-violet-700 text-white border-0"
            >
              <Search className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          </div>

          {/* Tab toggle */}
          <div
            role="tablist"
            className="flex items-center rounded-md border bg-muted/30 p-0.5 gap-0.5"
          >
            <button
              role="tab"
              aria-selected={activeTab === "portfolio"}
              onClick={() => setActiveTab("portfolio")}
              className={`px-3 py-1.5 rounded text-xs font-medium transition-colors ${
                activeTab === "portfolio"
                  ? "bg-background shadow-sm text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              Portfolio
            </button>
            <button
              role="tab"
              aria-selected={activeTab === "studios"}
              onClick={() => setActiveTab("studios")}
              className={`px-3 py-1.5 rounded text-xs font-medium transition-colors ${
                activeTab === "studios"
                  ? "bg-background shadow-sm text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              Studios
            </button>
          </div>

          {/* Location state */}
          <div className="flex items-center gap-1.5 ml-auto">
            {isGeoLocating ? (
              <span className="text-xs text-muted-foreground animate-pulse flex items-center gap-1">
                <Locate className="h-3 w-3" aria-hidden="true" />
                Detecting…
              </span>
            ) : locationName ? (
              <>
                {/* "Near me" toggle — shown only on portfolio tab */}
                {activeTab === "portfolio" && lat !== null && (
                  <button
                    type="button"
                    onClick={() => setNearOnly((v) => !v)}
                    className={`flex items-center gap-1 text-xs px-2 py-1 rounded-full border
                                transition-colors ${
                                  nearOnly
                                    ? "bg-violet-600/20 border-violet-500/60 text-violet-300"
                                    : "border-border text-muted-foreground hover:text-foreground"
                                }`}
                  >
                    <MapPin className="h-3 w-3" aria-hidden="true" />
                    Near me
                  </button>
                )}
                <span className="text-xs text-muted-foreground hidden sm:block truncate max-w-[140px]">
                  {locationName}
                </span>
              </>
            ) : hasGeo ? (
              <button
                type="button"
                onClick={handleUseMyLocation}
                className="flex items-center gap-1 text-xs text-violet-400
                           hover:text-violet-300 transition-colors"
              >
                <Locate className="h-3 w-3" aria-hidden="true" />
                Use my location
              </button>
            ) : null}
          </div>
        </div>

        {searchError && (
          <p className="px-4 pb-2 text-xs text-destructive" role="alert">{searchError}</p>
        )}
      </header>

      {/* ── Content area ─────────────────────────────────────────────────── */}
      <main className="flex-1 px-4 py-5 max-w-6xl mx-auto w-full">

        {/* Portfolio tab */}
        {activeTab === "portfolio" && (
          <PortfolioFeed
            lat={lat}
            lng={lng}
            radiusKm={radiusKm}
            nearOnly={nearOnly}
          />
        )}

        {/* Studios tab */}
        {activeTab === "studios" && (
          <div className="space-y-4">
            {/* Radius selector */}
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">Within</span>
              <select
                value={radiusKm}
                onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
                aria-label="Search radius"
                className="h-9 rounded-md border bg-background px-3 text-sm text-foreground
                           focus:outline-none focus:ring-2 focus:ring-ring"
              >
                {RADII.map((r) => (
                  <option key={r} value={r}>{r} km</option>
                ))}
              </select>
            </div>

            {isLoadingStudios ? (
              <StudioSkeleton />
            ) : !studios || studios.length === 0 ? (
              <div className="flex flex-col items-center gap-4 py-20 text-center">
                <MapPin className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
                <div className="space-y-1">
                  <p className="text-base font-semibold">No studios found nearby</p>
                  <p className="text-sm text-muted-foreground">Try a larger radius.</p>
                </div>
                <Link to="/register"
                  className="text-sm text-violet-400 hover:text-violet-300 underline underline-offset-4">
                  Register your studio →
                </Link>
              </div>
            ) : (
              <>
                <p
                  className="text-sm font-medium"
                  aria-live="polite"
                  aria-atomic="true"
                >
                  {studios.length} studio{studios.length !== 1 ? "s" : ""} near{" "}
                  <span className="text-foreground/70">{locationName}</span>{" "}
                  within {radiusKm} km
                </p>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {studios.map((s) => (
                    <StudioCard key={s.studioId} studio={s} />
                  ))}
                </div>
              </>
            )}
          </div>
        )}
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <footer className="py-4 text-center text-xs text-foreground/50 border-t">
        <a href="https://penaearte.com" target="_blank" rel="noopener noreferrer"
           className="hover:text-foreground/80 hover:underline transition-colors">
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
