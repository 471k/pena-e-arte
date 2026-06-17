"use client";
import "leaflet/dist/leaflet.css";
import L from "leaflet";
import { useEffect, useRef, useState } from "react";
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from "react-leaflet";
import { Loader2, LocateFixed, MapPin } from "lucide-react";
import { Button } from "./button";
import { cn } from "@/shared/utils/cn";

// ── custom pin ───────────────────────────────────────────────────────────────

const PIN_ICON = L.divIcon({
  className: "",
  html: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 36" width="24" height="36">
    <path d="M12 0C5.373 0 0 5.373 0 12c0 9 12 24 12 24S24 21 24 12C24 5.373 18.627 0 12 0z" fill="#e11d48"/>
    <circle cx="12" cy="12" r="4.5" fill="white"/>
  </svg>`,
  iconSize:   [24, 36],
  iconAnchor: [12, 36],
});

const DEFAULT_CENTER: [number, number] = [38.7169, -9.1395];
const DEFAULT_ZOOM = 5;

// ── geocoding helpers ────────────────────────────────────────────────────────

async function reverseGeocode(lat: number, lng: number): Promise<{ city: string; country: string }> {
  try {
    const r = await fetch(
      `https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${lat}&lon=${lng}`,
      { headers: { "Accept-Language": "en" } }
    );
    const d: { address?: Record<string, string> } = await r.json();
    const a = d.address ?? {};
    return {
      city:    a.city ?? a.town ?? a.village ?? a.municipality ?? a.county ?? "",
      country: a.country ?? "",
    };
  } catch {
    return { city: "", country: "" };
  }
}

async function detectIpLocation(): Promise<{ lat: number; lng: number } | null> {
  try {
    const r = await fetch("https://ipapi.co/json/");
    const d: { latitude?: number; longitude?: number } = await r.json();
    if (d.latitude != null && d.longitude != null) return { lat: d.latitude, lng: d.longitude };
  } catch { /* silent */ }
  return null;
}

// ── inner map helpers ────────────────────────────────────────────────────────

function ClickHandler({ onPin }: { onPin: (lat: number, lng: number) => void }) {
  useMapEvents({ click: (e) => onPin(e.latlng.lat, e.latlng.lng) });
  return null;
}

function FlyTo({ lat, lng, zoom }: { lat: number; lng: number; zoom: number }) {
  const map  = useMap();
  const prev = useRef("");
  useEffect(() => {
    const key = `${lat},${lng},${zoom}`;
    if (key !== prev.current) {
      prev.current = key;
      map.flyTo([lat, lng], zoom, { duration: 1.0 });
    }
  }, [map, lat, lng, zoom]);
  return null;
}

// ── public types ─────────────────────────────────────────────────────────────

export interface LocationPickerValue {
  lat:  number;
  lng:  number;
  city: string;
}

interface LocationPickerProps {
  value?:    LocationPickerValue;
  onChange:  (val: LocationPickerValue) => void;
  error?:    string;
  className?: string;
}

// ── component ────────────────────────────────────────────────────────────────

export function LocationPicker({ value, onChange, error, className }: LocationPickerProps) {
  const hasInitial = value != null && !isNaN(value.lat) && !isNaN(value.lng) && value.lat !== 0;

  const [pin,       setPin]       = useState<[number, number] | null>(
    hasInitial ? [value.lat, value.lng] : null
  );
  const [label,     setLabel]     = useState<{ city: string; country: string } | null>(
    hasInitial && value.city ? { city: value.city, country: "" } : null
  );
  const [resolving, setResolving] = useState(false);
  const [flyTarget, setFlyTarget] = useState<{ lat: number; lng: number; zoom: number } | null>(
    hasInitial ? { lat: value.lat, lng: value.lng, zoom: 13 } : null
  );
  const [locating,  setLocating]  = useState(false);

  // Sync pin/label/flyTarget when value arrives externally (e.g. form reset from API).
  // useState only initialises once on mount; if value wasn't available then (async load),
  // this effect catches the update without needing a key-driven remount.
  useEffect(() => {
    if (value == null || isNaN(value.lat) || value.lat === 0) return;
    setPin([value.lat, value.lng]);
    if (value.city) setLabel({ city: value.city, country: "" });
    setFlyTarget({ lat: value.lat, lng: value.lng, zoom: 13 });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value?.lat, value?.lng]);

  // Auto-detect location on first mount when there's no existing pin
  useEffect(() => {
    if (hasInitial) return;
    if ("geolocation" in navigator) {
      navigator.geolocation.getCurrentPosition(
        (pos) => setFlyTarget({ lat: pos.coords.latitude, lng: pos.coords.longitude, zoom: 12 }),
        async () => {
          const loc = await detectIpLocation();
          if (loc) setFlyTarget({ lat: loc.lat, lng: loc.lng, zoom: 10 });
        },
        { timeout: 6000 }
      );
    } else {
      detectIpLocation().then((loc) => {
        if (loc) setFlyTarget({ lat: loc.lat, lng: loc.lng, zoom: 10 });
      });
    }
    // intentionally run once on mount only
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handlePin(lat: number, lng: number) {
    setPin([lat, lng]);
    setResolving(true);
    const geo = await reverseGeocode(lat, lng);
    setResolving(false);
    setLabel(geo);
    onChange({ lat, lng, city: geo.city });
  }

  function handleUseMyLocation() {
    if (!("geolocation" in navigator)) return;
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLocating(false);
        const { latitude: lat, longitude: lng } = pos.coords;
        setFlyTarget({ lat, lng, zoom: 15 });
        await handlePin(lat, lng);
      },
      () => setLocating(false),
      { timeout: 8000 }
    );
  }

  return (
    <div className={cn("space-y-1.5", className)}>
      {/* Map */}
      <div className="relative rounded-md overflow-hidden border border-input h-[260px]">
        <MapContainer
          center={DEFAULT_CENTER}
          zoom={DEFAULT_ZOOM}
          className="h-full w-full"
          zoomControl
        >
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <ClickHandler onPin={handlePin} />
          {flyTarget && (
            <FlyTo lat={flyTarget.lat} lng={flyTarget.lng} zoom={flyTarget.zoom} />
          )}
          {pin && (
            <Marker
              position={pin}
              icon={PIN_ICON}
              draggable
              eventHandlers={{
                dragend: (e) => {
                  const { lat, lng } = (e.target as L.Marker).getLatLng();
                  handlePin(lat, lng);
                },
              }}
            />
          )}
        </MapContainer>

        {/* "My location" button */}
        <div className="absolute top-2 right-2 z-[1000]">
          <Button
            type="button"
            size="sm"
            variant="secondary"
            className="h-8 gap-1.5 shadow-md text-xs"
            onClick={handleUseMyLocation}
            disabled={locating}
          >
            {locating
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
              : <LocateFixed className="h-3.5 w-3.5" />}
            My location
          </Button>
        </div>

        {/* Hint before any pin */}
        {!pin && !locating && (
          <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-[1000] bg-background/90 backdrop-blur-sm border rounded-full px-3 py-1 text-xs text-muted-foreground shadow-sm pointer-events-none whitespace-nowrap">
            Click the map to pin your studio
          </div>
        )}
      </div>

      {/* Summary row */}
      <div className="flex items-center justify-between text-xs px-0.5 min-h-4">
        <span
          className={cn(
            "flex items-center gap-1",
            error && !label ? "text-destructive" : "text-muted-foreground"
          )}
        >
          {resolving ? (
            <>
              <Loader2 className="h-3 w-3 animate-spin" />
              Resolving location…
            </>
          ) : label ? (
            <>
              <MapPin className="h-3 w-3 shrink-0" />
              <span>
                <strong>{label.city || "Unknown city"}</strong>
                {label.country && `, ${label.country}`}
              </span>
            </>
          ) : (
            <span>{error ?? "No location pinned yet"}</span>
          )}
        </span>
        {pin && !resolving && (
          <span className="font-mono text-[11px] text-muted-foreground">
            {pin[0].toFixed(5)}, {pin[1].toFixed(5)}
          </span>
        )}
      </div>

      {/* Validation error when there's a pin but city is empty */}
      {error && label && (
        <p className="text-xs text-destructive">{error}</p>
      )}
    </div>
  );
}
