import { useId, useState } from "react";
import { cn } from "@/shared/utils/cn";

interface Zone {
  id:       string;
  label:    string;
  svgLabel: string;
  x:        number;
  y:        number;
  w:        number;
  h:        number;
  rx?:      number;
}

// viewBox "0 0 200 380" — zones are non-overlapping, person's left = viewer's left (the front view reads as a mirror)
export const FRONT_ZONES: Zone[] = [
  { id: "head",             label: "Head",           svgLabel: "Head",     x: 68,  y: 5,   w: 64,  h: 62,  rx: 32 },
  { id: "neck",             label: "Neck",           svgLabel: "Neck",     x: 84,  y: 67,  w: 32,  h: 20 },
  { id: "left_shoulder",    label: "Left Shoulder",  svgLabel: "L.Shldr",  x: 22,  y: 87,  w: 36,  h: 40 },
  { id: "chest",            label: "Chest",          svgLabel: "Chest",    x: 58,  y: 87,  w: 84,  h: 60 },
  { id: "right_shoulder",   label: "Right Shoulder", svgLabel: "R.Shldr",  x: 142, y: 87,  w: 36,  h: 40 },
  { id: "abdomen",          label: "Abdomen",        svgLabel: "Abdomen",  x: 60,  y: 147, w: 80,  h: 53 },
  { id: "left_upper_arm",   label: "Left Upper Arm", svgLabel: "L.Arm",    x: 22,  y: 127, w: 36,  h: 60 },
  { id: "right_upper_arm",  label: "Right Upper Arm",svgLabel: "R.Arm",    x: 142, y: 127, w: 36,  h: 60 },
  { id: "left_forearm",     label: "Left Forearm",   svgLabel: "L.Forarm", x: 18,  y: 187, w: 36,  h: 60 },
  { id: "right_forearm",    label: "Right Forearm",  svgLabel: "R.Forarm", x: 146, y: 187, w: 36,  h: 60 },
  { id: "left_hand",        label: "Left Hand",      svgLabel: "L.Hand",   x: 16,  y: 247, w: 36,  h: 35 },
  { id: "right_hand",       label: "Right Hand",     svgLabel: "R.Hand",   x: 148, y: 247, w: 36,  h: 35 },
  { id: "left_thigh",       label: "Left Thigh",     svgLabel: "L.Thigh",  x: 60,  y: 200, w: 38,  h: 80 },
  { id: "right_thigh",      label: "Right Thigh",    svgLabel: "R.Thigh",  x: 102, y: 200, w: 38,  h: 80 },
  { id: "left_knee",        label: "Left Knee",      svgLabel: "L.Knee",   x: 60,  y: 280, w: 38,  h: 25 },
  { id: "right_knee",       label: "Right Knee",     svgLabel: "R.Knee",   x: 102, y: 280, w: 38,  h: 25 },
  { id: "left_shin",        label: "Left Shin",      svgLabel: "L.Shin",   x: 60,  y: 305, w: 36,  h: 55 },
  { id: "right_shin",       label: "Right Shin",     svgLabel: "R.Shin",   x: 104, y: 305, w: 36,  h: 55 },
  { id: "left_foot",        label: "Left Foot",      svgLabel: "L.Foot",   x: 52,  y: 360, w: 46,  h: 18 },
  { id: "right_foot",       label: "Right Foot",     svgLabel: "R.Foot",   x: 102, y: 360, w: 46,  h: 18 },
];

export const BACK_ZONES: Zone[] = [
  { id: "skull",                 label: "Skull",             svgLabel: "Skull",    x: 68,  y: 5,   w: 64,  h: 62,  rx: 32 },
  { id: "neck_back",             label: "Neck (Back)",       svgLabel: "Neck",     x: 84,  y: 67,  w: 32,  h: 20 },
  { id: "left_shoulder_back",    label: "Left Shoulder",     svgLabel: "L.Shldr",  x: 22,  y: 87,  w: 36,  h: 40 },
  { id: "upper_back",            label: "Upper Back",        svgLabel: "UpperBk",  x: 58,  y: 87,  w: 84,  h: 55 },
  { id: "right_shoulder_back",   label: "Right Shoulder",    svgLabel: "R.Shldr",  x: 142, y: 87,  w: 36,  h: 40 },
  { id: "lower_back",            label: "Lower Back",        svgLabel: "LowerBk",  x: 60,  y: 142, w: 80,  h: 58 },
  { id: "left_upper_arm_back",   label: "Left Upper Arm",    svgLabel: "L.Arm",    x: 22,  y: 127, w: 36,  h: 60 },
  { id: "right_upper_arm_back",  label: "Right Upper Arm",   svgLabel: "R.Arm",    x: 142, y: 127, w: 36,  h: 60 },
  { id: "left_forearm_back",     label: "Left Forearm",      svgLabel: "L.Forarm", x: 18,  y: 187, w: 36,  h: 60 },
  { id: "right_forearm_back",    label: "Right Forearm",     svgLabel: "R.Forarm", x: 146, y: 187, w: 36,  h: 60 },
  { id: "left_hand_back",        label: "Left Hand",         svgLabel: "L.Hand",   x: 16,  y: 247, w: 36,  h: 35 },
  { id: "right_hand_back",       label: "Right Hand",        svgLabel: "R.Hand",   x: 148, y: 247, w: 36,  h: 35 },
  { id: "left_buttock",          label: "Left Buttock",      svgLabel: "L.Buttk",  x: 60,  y: 200, w: 38,  h: 45 },
  { id: "right_buttock",         label: "Right Buttock",     svgLabel: "R.Buttk",  x: 102, y: 200, w: 38,  h: 45 },
  { id: "left_thigh_back",       label: "Left Thigh (Back)", svgLabel: "L.Thigh",  x: 60,  y: 245, w: 38,  h: 35 },
  { id: "right_thigh_back",      label: "Right Thigh (Back)",svgLabel: "R.Thigh",  x: 102, y: 245, w: 38,  h: 35 },
  { id: "left_knee_back",        label: "Left Knee (Back)",  svgLabel: "L.Knee",   x: 60,  y: 280, w: 38,  h: 25 },
  { id: "right_knee_back",       label: "Right Knee (Back)", svgLabel: "R.Knee",   x: 102, y: 280, w: 38,  h: 25 },
  { id: "left_calf",             label: "Left Calf",         svgLabel: "L.Calf",   x: 60,  y: 305, w: 36,  h: 55 },
  { id: "right_calf",            label: "Right Calf",        svgLabel: "R.Calf",   x: 104, y: 305, w: 36,  h: 55 },
  { id: "left_heel",             label: "Left Heel/Foot",    svgLabel: "L.Heel",   x: 52,  y: 360, w: 46,  h: 18 },
  { id: "right_heel",            label: "Right Heel/Foot",   svgLabel: "R.Heel",   x: 102, y: 360, w: 46,  h: 18 },
];

export const ALL_BODY_ZONES = [...FRONT_ZONES, ...BACK_ZONES];

type Side = "front" | "back";

const BACK_IDS = new Set(BACK_ZONES.map((z) => z.id));

function sideOf(id: string): Side {
  return BACK_IDS.has(id) ? "back" : "front";
}

/** "Left Thigh (Back)" -> "Left Thigh": inside a Front/Back group the side is already said. */
function baseLabel(zone: Zone): string {
  return zone.label.replace(/\s*\(Back\)$/, "");
}

/**
 * The full name of a body area, front/back included, e.g. "Left Shoulder · back" (C10). Two areas share
 * "Left Shoulder" (front and back), so a summary that drops the side is ambiguous.
 */
export function bodyZoneLabel(id: string): string | null {
  const zone = ALL_BODY_ZONES.find((z) => z.id === id);
  return zone ? `${baseLabel(zone)} · ${sideOf(id)}` : null;
}

interface BodyMapProps {
  locations: string[];
  readOnly?: boolean;
  onChange?: (locations: string[]) => void;
  /** The editable group's name, e.g. "Desired placement". */
  legend?: string;
  /** Hide the legend visually when a heading right above already says it (it stays for assistive tech). */
  legendHidden?: boolean;
  /** Whose body this is, for the left/right note: the viewer's own, or a client's. */
  perspective?: "self" | "client";
}

/**
 * Body areas, selectable without a mouse (C10, `requirements.md`). Editable, it is a fieldset of labelled
 * checkboxes in Front and Back groups, and the figure is a pointer enhancement kept in sync with them. The
 * figure is hidden from assistive tech, as in the package prototype: the checkboxes are the control, and a
 * second, focusable copy of the same 40 choices would only double the tab stops.
 *
 * Left and right are always the person's own. The front view draws the person's left on the viewer's left
 * (as in a mirror) — unchanged here, because the zone ids and their meaning are stored data.
 */
export function BodyMap({
  locations, readOnly = false, onChange, legend = "Body areas", legendHidden = false, perspective = "self",
}: BodyMapProps) {
  const [view, setView] = useState<Side>("front");
  const groupId = useId();
  const selected = new Set(locations);
  const zones = view === "front" ? FRONT_ZONES : BACK_ZONES;
  const editable = !readOnly && onChange !== undefined;

  function toggle(id: string) {
    if (readOnly || !onChange) return;
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onChange([...next]);
  }

  // Ticking a back area shows the back of the figure, so the map visibly follows the list.
  function toggleFromList(id: string) {
    setView(sideOf(id));
    toggle(id);
  }

  const marked = locations
    .map((id) => ({ id, label: bodyZoneLabel(id) }))
    .filter((m): m is { id: string; label: string } => m.label !== null);

  const figure = (
    <div className="space-y-2">
      <div className="flex justify-center gap-1">
        {(["front", "back"] as const).map((v) => (
          <button
            key={v}
            type="button"
            aria-pressed={view === v}
            aria-label={v === "front" ? "Front of the figure" : "Back of the figure"}
            onClick={() => setView(v)}
            className={cn(
              "px-4 py-1 text-xs rounded-md transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              view === v
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {v === "front" ? "Front" : "Back"}
          </button>
        ))}
      </div>

      {/* Pointer enhancement only: the checkboxes (editable) or the summary list (read-only) carry the meaning. */}
      <svg
        viewBox="0 0 200 380"
        className="w-full max-w-[190px] mx-auto block select-none"
        aria-hidden="true"
        data-testid="body-map-figure"
      >
        {zones.map((zone) => {
          const on = selected.has(zone.id);
          const cx = zone.x + zone.w / 2;
          const cy = zone.y + zone.h / 2;
          return (
            <g
              key={zone.id}
              data-zone={zone.id}
              onClick={() => toggle(zone.id)}
              style={{ cursor: editable ? "pointer" : "default" }}
            >
              <title>{zone.label}</title>
              <rect
                x={zone.x} y={zone.y} width={zone.w} height={zone.h}
                rx={zone.rx ?? 3}
                style={{
                  fill:    on ? "var(--color-primary)"    : "var(--color-muted)",
                  opacity: on ? 0.9 : 0.55,
                  stroke:  "var(--color-border)",
                  strokeWidth: "1",
                  transition: "opacity 0.1s, fill 0.1s",
                }}
              />
              <text
                x={cx} y={cy + 1}
                textAnchor="middle"
                dominantBaseline="central"
                style={{
                  fontSize: "6.5px",
                  fill: on ? "var(--color-primary-foreground)" : "var(--color-muted-foreground)",
                  pointerEvents: "none",
                  fontFamily: "Fraunces, sans-serif",
                }}
              >
                {zone.svgLabel}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );

  const summary = marked.length > 0 ? (
    <ul className="flex flex-wrap gap-1" aria-label={editable ? "Selected areas" : "Marked body areas"}>
      {marked.map(({ id, label }) => (
        <li key={id} className="text-xs bg-primary/10 text-link px-2 py-0.5 rounded-full">
          {label}
        </li>
      ))}
    </ul>
  ) : (
    <p className="text-xs text-muted-foreground text-center">
      {editable ? "No areas selected yet." : "No body areas recorded."}
    </p>
  );

  if (!editable) {
    return (
      <div className="space-y-3">
        {figure}
        {summary}
      </div>
    );
  }

  const whose = perspective === "self" ? "your own" : "the client's own";
  const groups: [Side, Zone[]][] = [["front", FRONT_ZONES], ["back", BACK_ZONES]];

  return (
    <fieldset className="space-y-3 min-w-0" aria-describedby={`${groupId}-hint`}>
      <legend className={cn("text-sm font-medium", legendHidden && "sr-only")}>{legend}</legend>
      <p id={`${groupId}-hint`} className="text-xs text-muted-foreground">
        Left and right mean {whose} left and right. Choose as many areas as you need, from the lists or on the figure.
      </p>
      {figure}
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        {groups.map(([side, list]) => (
          <fieldset key={side} className="min-w-0">
            <legend className="text-xs font-medium text-muted-foreground mb-1">
              {side === "front" ? "Front of body" : "Back of body"}
            </legend>
            <ul className="space-y-0.5">
              {list.map((zone) => {
                const inputId = `${groupId}-${zone.id}`;
                return (
                  <li key={zone.id}>
                    <label htmlFor={inputId} className="flex items-center gap-2 py-1 text-sm cursor-pointer">
                      {/* The name carries the side ("Head, front"); an sr-only span would be read with a stray
                          space before the comma in Chrome. Starts with the visible text, so voice control works. */}
                      <input
                        id={inputId}
                        type="checkbox"
                        aria-label={`${baseLabel(zone)}, ${side}`}
                        className="h-4 w-4 shrink-0 accent-primary"
                        checked={selected.has(zone.id)}
                        onChange={() => toggleFromList(zone.id)}
                      />
                      <span>{baseLabel(zone)}</span>
                    </label>
                  </li>
                );
              })}
            </ul>
          </fieldset>
        ))}
      </div>
      {summary}
    </fieldset>
  );
}
