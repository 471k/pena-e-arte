import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Locate, MapPin, Search, Users } from "lucide-react";
import { Button }         from "@/shared/components/ui/button";
import { Skeleton }       from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { StarRating }     from "@/shared/components/ui/StarRating";
import { PortfolioFeed }  from "./PortfolioFeed";
import { AuthenticatedNav, BrandMark } from "./PublicPageHeader";
import {
  useGetNearbyStudiosQuery,
  type NearbyStudioResponse,
} from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";

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
    title:       "Discover Tattoo Art Near You — TattooOS",
    description: "Browse tattoo portfolios and studios near your location.",
    canonical:   "https://tattooos.co/discover",
  });
  return null;
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function DiscoverPage() {
  // window.isSecureContext guards against Chrome logging "A Geolocation request can
  // only be fulfilled in a secure context" on every mount when the app is reached over
  // plain HTTP on a non-localhost origin (e.g. a LAN IP) — the API object is present
  // either way, but calling it there is a guaranteed, noisy failure.
  const hasGeo = "geolocation" in navigator && window.isSecureContext;
  const token  = useAppSelector((s) => s.auth.token);
  const user   = useAppSelector((s) => s.auth.user);
  const role   = useAppSelector((s) => s.auth.role);

  const [lat,           setLat]           = useState<number | null>(hasGeo ? null : DEFAULT_LAT);
  const [lng,           setLng]           = useState<number | null>(hasGeo ? null : DEFAULT_LNG);
  const [locationName,  setLocationName]  = useState<string>(hasGeo ? "" : DEFAULT_CITY);
  const [isGeoLocating, setIsGeoLocating] = useState<boolean>(hasGeo);
  const [radiusKm,      setRadiusKm]      = useState<Radius>(50);
  const [searchInput,   setSearchInput]   = useState<string>("");
  const [searchError,   setSearchError]   = useState<string | null>(null);
  const [isGeocoding,   setIsGeocoding]   = useState(false);
  // Default on: once the user's location resolves, the feed should immediately
  // prioritize nearby work rather than requiring an extra click on "Near me"
  // (the header already displays the resolved location, implying it's in use).
  const [nearOnly,      setNearOnly]      = useState(true);

  // Tracks WHY lat/lng currently holds the value it does — needed because a
  // keyword search must ignore ambient location ("geo"/"default") but must
  // respect an explicit city search ("search"). See PortfolioFeed.tsx's
  // useLocationScope for how this is consumed.
  type LocationSource = "geo" | "default" | "search";
  const [locationSource, setLocationSource] = useState<LocationSource>(hasGeo ? "geo" : "default");

  // Free-text content search ("What"), independent of the existing city
  // search ("Where", searchInput below). Debounced the same setTimeout/cleanup
  // way useAddressGeocode.ts debounces its own input — a timer side-effect,
  // not data fetching; RTK Query still owns the actual request.
  const [keywordInput, setKeywordInput] = useState("");
  const [keyword,      setKeyword]      = useState("");

  useEffect(() => {
    const id = setTimeout(() => setKeyword(keywordInput.trim()), 400);
    return () => clearTimeout(id);
  }, [keywordInput]);

  // Tab state lives in the URL so a shared /discover?tab=studios link opens on the
  // right tab instead of always defaulting to portfolio.
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab: ActiveTab = searchParams.get("tab") === "studios" ? "studios" : "portfolio";
  function setActiveTab(tab: ActiveTab) {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (tab === "portfolio") next.delete("tab");
      else next.set("tab", tab);
      return next;
    }, { replace: true });
  }

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

  // Geolocation denied/unavailable/timed out — don't silently pretend DEFAULT_CITY was
  // the user's detected location: that combined with nearOnly's default 50km radius
  // made real, unrelated-city results disappear with no indication anything failed.
  // Falling back to the default anchor with nearOnly off shows all results instead.
  function handleGeoFailure() {
    setLat(DEFAULT_LAT);
    setLng(DEFAULT_LNG);
    setLocationName(DEFAULT_CITY);
    setLocationSource("default");
    setNearOnly(false);
    setIsGeoLocating(false);
  }

  // ── Geolocation on mount ──────────────────────────────────────────────────
  // Acceptable useEffect: browser API side-effect, not data fetching.
  useEffect(() => {
    if (!hasGeo) return;
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setIsGeoLocating(false);
        setLocationSource("geo");
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => handleGeoFailure(),
      // 8s was too tight for a cold location fix (Windows Location Services can take
      // several seconds to warm up on the first request of a session) — a real,
      // reproduced timeout on first mount fell through to handleGeoFailure below even
      // though a retry moments later resolved correctly.
      { timeout: 15_000, maximumAge: 60_000 },
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
        setLocationSource("geo");
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => handleGeoFailure(),
      { timeout: 15_000, maximumAge: 0 },
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
      setLocationSource("search");
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
          <BrandMark />

          <nav className="flex items-center gap-1" aria-label="Site navigation">
            <Link to="/map"
              className="text-xs text-muted-foreground hover:text-foreground
                         transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
              Map
            </Link>

            {token ? (
              <AuthenticatedNav user={user} role={role} />
            ) : (
              <>
                <Link to="/login"
                  className="text-xs text-muted-foreground hover:text-foreground
                             transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
                  Sign in
                </Link>
                <Link to="/client-register"
                  className="text-xs text-muted-foreground hover:text-foreground
                             transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
                  Sign up
                </Link>
                <Link to="/register"
                  className="text-xs font-medium px-3 py-2 rounded-md
                             border-2 border-violet-500 text-violet-400
                             bg-violet-500/5
                             hover:bg-violet-500/15 hover:text-violet-300
                             transition-colors
                             focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-500">
                  Register studio
                </Link>
              </>
            )}
          </nav>
        </div>

        {/* Value-prop strip for logged-out visitors */}
        {!token && (
          <div className="px-4 pt-1 pb-2.5 border-b border-border/40">
            <p className="text-xs text-muted-foreground max-w-sm">
              Discover tattoo artists and studios near you. Browse portfolios, read
              reviews, and book your next session.
            </p>
          </div>
        )}

        {/* Bottom row: search + tabs + location toggle. flex-wrap: with two search
            inputs the cluster no longer fits beside the tabs/location chip below
            ~640px. */}
        <div className="flex flex-wrap items-center gap-2 px-4 pb-2.5 pt-2.5">
          {/* Search cluster — "What" (keyword, Portfolio tab only) + "Where" (city, unchanged) */}
          <div className="flex flex-1 min-w-full sm:min-w-0 items-center gap-2 max-w-full sm:max-w-lg">
            {activeTab === "portfolio" && (
              <div className="relative flex-1 min-w-[110px]">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                                   text-muted-foreground pointer-events-none" aria-hidden="true" />
                <input
                  type="search"
                  placeholder="Search styles, artists…"
                  value={keywordInput}
                  onChange={(e) => setKeywordInput(e.target.value)}
                  aria-label="Search tattoo styles or artist names"
                  className="w-full h-9 pl-8 pr-3 rounded-md border bg-background text-xs
                             focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-1
                             placeholder:text-muted-foreground"
                />
              </div>
            )}
            <div className="relative flex-1 min-w-[110px]">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                                 text-muted-foreground pointer-events-none" aria-hidden="true" />
              <input
                ref={inputRef}
                type="search"
                placeholder="Find artists in a city…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
                aria-label="Search for a city to discover artists"
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

          {/* Tab toggle — underline style with pronounce active state */}
          <div
            role="tablist"
            aria-label="Content type"
            className="flex items-center gap-0 border-b border-border/40"
          >
            {(["portfolio", "studios"] as const).map((tab) => (
              <button
                key={tab}
                role="tab"
                id={`tab-${tab}`}
                aria-selected={activeTab === tab}
                aria-controls={`panel-${tab}`}
                onClick={() => setActiveTab(tab)}
                className={`px-4 py-2 text-xs font-medium transition-colors capitalize
                            border-b-2 -mb-px
                            ${activeTab === tab
                              ? "border-violet-500 text-foreground font-semibold"
                              : "border-transparent text-muted-foreground hover:text-foreground"
                            }`}
              >
                {tab === "portfolio" ? "Portfolio" : "Studios"}
              </button>
            ))}
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
                    aria-pressed={nearOnly}
                    aria-label={nearOnly
                      ? "Location filter active — click to show all"
                      : "Filter to near me"}
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
                <span aria-hidden="true" className="text-border select-none hidden sm:block">·</span>
                <span className="text-xs text-muted-foreground hidden sm:block truncate max-w-[140px]"
                      aria-label={`Current location: ${locationName}`}>
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

        {/* Value-prop hero for first-time logged-out visitors (before location is known) */}
        {!token && !locationName && (
          <section aria-labelledby="hero-heading" className="py-10 text-center space-y-3">
            <h1 id="hero-heading" className="text-2xl font-bold tracking-tight">
              Discover tattoo artists near you
            </h1>
            <p className="text-sm text-muted-foreground max-w-md mx-auto">
              Browse portfolios from studios worldwide, filter by style, and find the
              artist who matches your vision.
            </p>
            <div className="flex items-center justify-center gap-3 pt-1">
              <Link to="/login"
                className="text-sm text-violet-400 hover:text-violet-300 underline
                           underline-offset-4 transition-colors">
                Sign in
              </Link>
              <span aria-hidden="true" className="text-border">·</span>
              <Link to="/client-register"
                className="text-sm text-violet-400 hover:text-violet-300 underline
                           underline-offset-4 transition-colors">
                Sign up as a client
              </Link>
              <span aria-hidden="true" className="text-border">·</span>
              <Link to="/register"
                className="text-sm font-medium px-4 py-1.5 rounded-md
                           bg-violet-600 hover:bg-violet-700 text-white transition-colors">
                Register your studio
              </Link>
            </div>
          </section>
        )}

        {/* Tab panel with fade transition on switch */}
        <div key={activeTab} className="animate-in fade-in">
          {/* Portfolio tab */}
          <div
            id="panel-portfolio"
            role="tabpanel"
            aria-labelledby="tab-portfolio"
            hidden={activeTab !== "portfolio"}
          >
            {activeTab === "portfolio" && (
              <PortfolioFeed
                lat={lat}
                lng={lng}
                radiusKm={radiusKm}
                nearOnly={nearOnly}
                keyword={keyword}
                locationSource={locationSource}
                locationLabel={locationName}
              />
            )}
          </div>

          {/* Studios tab */}
          <div
            id="panel-studios"
            role="tabpanel"
            aria-labelledby="tab-studios"
            hidden={activeTab !== "studios"}
          >
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
          </div>
        </div>
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <footer className="py-5 border-t border-border/40">
        <div className="max-w-6xl mx-auto px-4 flex flex-col sm:flex-row items-center
                        justify-between gap-3 text-xs text-foreground/65">
          <span>© {new Date().getFullYear()} TattooOS. All rights reserved.</span>
          <nav aria-label="Footer links" className="flex items-center gap-4">
            <Link to="/map" className="hover:text-foreground/80 transition-colors">
              Map
            </Link>
            {!token && (
              <>
                <Link to="/client-register" className="hover:text-foreground/80 transition-colors">
                  Sign up
                </Link>
                <Link to="/register" className="hover:text-foreground/80 transition-colors">
                  Register studio
                </Link>
              </>
            )}
          </nav>
        </div>
      </footer>
    </div>
  );
}
