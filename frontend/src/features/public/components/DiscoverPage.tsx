import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Locate, MapPin, Search, Users } from "lucide-react";
import { Button }    from "@/shared/components/ui/button";
import { Skeleton }  from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { StarRating } from "@/shared/components/ui/StarRating";
import { useGetNearbyStudiosQuery, type NearbyStudioResponse } from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

// ── Constants ────────────────────────────────────────────────────────────────

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

const DEFAULT_LAT  = 38.7169;
const DEFAULT_LNG  = -9.1395;
const DEFAULT_CITY = "Lisbon, Portugal";

// ── Types ────────────────────────────────────────────────────────────────────

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Format distance with the numeric part at normal size and unit dimmed. */
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

// [AUDIT FIX #1 — Critical: pen icon placeholder replaced with neutral initials monogram]
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

// ── StudioCard ───────────────────────────────────────────────────────────────

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    // [AUDIT FIX — focus ring on keyboard-navigable card]
    <Link
      to={`/s/${studio.slug}`}
      className="block rounded-lg focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
    >
      <Card
        className="hover:border-border/80 hover:shadow-md hover:shadow-black/20
                   transition-all cursor-pointer h-full"
      >
        {/* Cover image or initials monogram */}
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
          // [AUDIT FIX #1 — no more PenLine icon as placeholder]
          <StudioMonogram name={studio.name} />
        )}

        <CardContent className="p-4 space-y-2">
          {/* [AUDIT FIX — name visual weight boosted] */}
          <p className="font-semibold text-sm leading-tight line-clamp-1">
            {studio.name}
          </p>

          {/* Rating row — [AUDIT QUICK WIN #1] */}
          <div className="flex items-center gap-1.5">
            {studio.reviewCount > 0 ? (
              <>
                <StarRating value={Math.round(studio.averageRating ?? 0)} />
                <span className="text-xs text-muted-foreground">
                  ({studio.reviewCount})
                </span>
              </>
            ) : (
              <span className="text-xs text-muted-foreground/60 italic">No reviews yet</span>
            )}
          </div>

          {/* Location + distance row */}
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            {/* [AUDIT FIX — aria-hidden on decorative icons] */}
            <MapPin className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="truncate">{studio.city}</span>
            {/* [AUDIT FIX — distance styling: number normal, unit dimmed] */}
            <span className="ml-auto font-medium text-foreground/80 whitespace-nowrap">
              <DistanceLabel km={studio.distanceKm} />
            </span>
          </div>

          {/* Artist count */}
          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" aria-hidden="true" />
              <span>
                {studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}
              </span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

// ── DiscoverSkeleton ─────────────────────────────────────────────────────────

function DiscoverSkeleton() {
  return (
    <div
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      aria-label="Loading studios"
      aria-busy="true"
    >
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

// ── Meta ─────────────────────────────────────────────────────────────────────

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Find Tattoo Studios Near You — Pena e Artë",
    description: "Browse tattoo studios and artists near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

// ── Main page ────────────────────────────────────────────────────────────────

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

  const inputRef = useRef<HTMLInputElement>(null);

  // [AUDIT FIX #2 — Critical: show "Near [City]" not "Your location"]
  const reverseGeocode = useCallback(async (latitude: number, longitude: number) => {
    try {
      const res = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=json&lat=${latitude}&lon=${longitude}`,
        { headers: { "Accept-Language": "en" } },
      );
      const data = (await res.json()) as {
        address?: { city?: string; town?: string; village?: string; country?: string };
      };
      const place = data.address?.city ?? data.address?.town ?? data.address?.village ?? "";
      const country = data.address?.country ?? "";
      setLocationName(place && country ? `${place}, ${country}` : place || DEFAULT_CITY);
    } catch {
      setLocationName(DEFAULT_CITY);
    }
  }, []);

  // This useEffect is acceptable: browser API side-effect, not data fetching.
  useEffect(() => {
    if (!hasGeo) return;

    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        const { latitude, longitude } = pos.coords;
        setLat(latitude);
        setLng(longitude);
        setIsGeoLocating(false);
        await reverseGeocode(latitude, longitude);
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

  // [AUDIT FIX — explicit "Use my location" affordance]
  function handleUseMyLocation() {
    if (!hasGeo) return;
    setIsGeoLocating(true);
    setLocationName("");
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        const { latitude, longitude } = pos.coords;
        setLat(latitude);
        setLng(longitude);
        setIsGeoLocating(false);
        await reverseGeocode(latitude, longitude);
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

  async function handleLocationSearch() {
    const q = searchInput.trim();
    if (!q) return;

    setIsGeocoding(true);
    setSearchError(null);

    try {
      const res = await fetch(
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

  const { data: studios, isLoading: isStudiosLoading, isFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: lat === null || lng === null },
    );

  const isLoadingStudios = lat === null || isStudiosLoading || isFetching;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <DiscoverMeta />

      {/* ── Nav ─────────────────────────────────────────────────────────── */}
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-[100]">
        {/* Brand */}
        <div className="flex items-center gap-2">
          {/* [AUDIT FIX — aria-hidden on decorative brand icon] */}
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            className="h-5 w-5 fill-none stroke-current stroke-2"
          >
            <path
              strokeLinecap="round" strokeLinejoin="round"
              d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828L11.828 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0 019 10.172V11z"
            />
          </svg>
          <span className="font-semibold tracking-tight">Pena e Artë</span>
        </div>

        {/* Nav actions */}
        <nav className="flex items-center gap-1" aria-label="Site navigation">
          {/* [AUDIT FIX — touch-friendly nav links with adequate py] */}
          <Link
            to="/map"
            className="text-sm text-muted-foreground hover:text-foreground
                       transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
          >
            View on map
          </Link>
          <Link
            to="/login"
            className="text-sm text-muted-foreground hover:text-foreground
                       transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
          >
            Sign in
          </Link>
          {/* [AUDIT FIX #3 — Critical: downgraded from filled white to violet outline] */}
          <Link
            to="/register"
            className="text-sm font-medium px-3 py-2 rounded-md border
                       border-violet-500/60 text-violet-400
                       hover:bg-violet-500/10 hover:border-violet-400
                       transition-colors"
          >
            Register your studio
          </Link>
        </nav>
      </header>

      {/* ── Hero + Search ────────────────────────────────────────────────── */}
      {/* [AUDIT FIX — centered hero section, more "landing page" feel] */}
      <section className="border-b bg-background/95">
        <div className="max-w-2xl mx-auto px-4 py-10 space-y-5 text-center">
          {/* Heading */}
          <div className="space-y-1.5">
            <h1 className="text-3xl font-bold tracking-tight">
              Find tattoo studios near you
            </h1>

            {/* Location subtitle — [AUDIT FIX #2: show city or explicit affordance] */}
            <div className="h-6 flex items-center justify-center gap-1.5">
              {isGeoLocating ? (
                <p className="text-sm text-muted-foreground animate-pulse">
                  Detecting your location…
                </p>
              ) : locationName ? (
                <p className="text-sm text-muted-foreground flex items-center gap-1">
                  <MapPin className="h-3.5 w-3.5" aria-hidden="true" />
                  Near {locationName}
                  {hasGeo && (
                    <>
                      <span aria-hidden="true" className="mx-1 opacity-30">·</span>
                      <button
                        type="button"
                        onClick={handleUseMyLocation}
                        className="text-violet-400 hover:text-violet-300 transition-colors
                                   underline underline-offset-2 text-xs"
                        disabled={isGeoLocating}
                      >
                        Update
                      </button>
                    </>
                  )}
                </p>
              ) : hasGeo ? (
                <button
                  type="button"
                  onClick={handleUseMyLocation}
                  className="text-sm text-violet-400 hover:text-violet-300
                             flex items-center gap-1.5 transition-colors"
                >
                  <Locate className="h-3.5 w-3.5" aria-hidden="true" />
                  Use my location
                </button>
              ) : null}
            </div>
          </div>

          {/* Search + radius row */}
          {/* [AUDIT FIX — search button touch target ≥ 44px; "Within" grouped as fieldset legend] */}
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            {/* Search input + button */}
            <div className="flex flex-1 gap-2">
              <input
                ref={inputRef}
                type="search"
                placeholder="Search a city or address…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
                className="flex-1 h-11 rounded-md border bg-background px-3 text-sm
                           focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-1
                           placeholder:text-muted-foreground"
                aria-label="Search for a city or address"
              />
              <Button
                onClick={() => void handleLocationSearch()}
                disabled={isGeocoding || !searchInput.trim()}
                aria-label="Search location"
                className="h-11 px-4 bg-violet-600 hover:bg-violet-700 text-white border-0"
              >
                <Search className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>

            {/* Radius control */}
            <fieldset className="flex items-center gap-2">
              {/* [AUDIT FIX — "Within" label now inside a proper fieldset/legend grouping] */}
              <legend className="text-xs text-muted-foreground whitespace-nowrap sr-only">
                Search radius
              </legend>
              <span className="text-xs text-muted-foreground whitespace-nowrap" aria-hidden="true">
                Within
              </span>
              <select
                id="radius-select"
                value={radiusKm}
                onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
                aria-label="Search radius"
                className="h-11 rounded-md border bg-background px-3 text-sm text-foreground
                           focus:outline-none focus:ring-2 focus:ring-ring"
              >
                {RADII.map((r) => (
                  <option key={r} value={r}>{r} km</option>
                ))}
              </select>
            </fieldset>
          </div>

          {/* Geocoding error */}
          {searchError && (
            <p className="text-sm text-destructive text-left" role="alert">
              {searchError}
            </p>
          )}
        </div>
      </section>

      {/* ── Results ──────────────────────────────────────────────────────── */}
      <main className="flex-1 max-w-5xl mx-auto w-full px-4 py-8 space-y-4">
        {isLoadingStudios ? (
          <DiscoverSkeleton />
        ) : !studios || studios.length === 0 ? (
          /* [AUDIT FIX — empty state with actionable messaging] */
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <div className="rounded-full bg-muted/40 p-5">
              <MapPin className="h-8 w-8 text-muted-foreground/50" aria-hidden="true" />
            </div>
            <div className="space-y-1">
              <p className="text-base font-semibold">No studios found nearby</p>
              <p className="text-sm text-muted-foreground max-w-xs">
                Try a larger radius, or search a different city.
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
        ) : (
          <>
            {/* Result count — [AUDIT FIX: larger, bolder, includes location reference, aria-live] */}
            <p
              className="text-sm font-medium"
              aria-live="polite"
              aria-atomic="true"
            >
              {studios.length} studio{studios.length !== 1 ? "s" : ""} near{" "}
              <span className="text-foreground/70">{locationName}</span>{" "}
              within {radiusKm} km
            </p>

            {/* Card grid */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {studios.map((s) => (
                <StudioCard key={s.studioId} studio={s} />
              ))}
            </div>
          </>
        )}
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      {/* [AUDIT FIX — better contrast on footer text] */}
      <footer className="py-4 text-center text-xs text-foreground/50 border-t">
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
