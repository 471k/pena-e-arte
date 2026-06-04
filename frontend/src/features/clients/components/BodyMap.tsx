import { useState } from "react";
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

// viewBox "0 0 200 380" — zones are non-overlapping, person's left = viewer's left
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

interface BodyMapProps {
  locations: string[];
  readOnly?: boolean;
  onChange?: (locations: string[]) => void;
}

export function BodyMap({ locations, readOnly = false, onChange }: BodyMapProps) {
  const [view, setView] = useState<"front" | "back">("front");
  const selected = new Set(locations);
  const zones = view === "front" ? FRONT_ZONES : BACK_ZONES;

  function toggle(id: string) {
    if (readOnly || !onChange) return;
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onChange([...next]);
  }

  const markedLabels = locations
    .map((id) => ALL_BODY_ZONES.find((z) => z.id === id)?.label)
    .filter(Boolean) as string[];

  return (
    <div className="space-y-3">
      {/* Front / Back tabs */}
      <div className="flex justify-center gap-1">
        {(["front", "back"] as const).map((v) => (
          <button
            key={v}
            type="button"
            onClick={() => setView(v)}
            className={cn(
              "px-4 py-1 text-xs rounded-md transition-colors",
              view === v
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {v === "front" ? "Front" : "Back"}
          </button>
        ))}
      </div>

      {/* SVG body figure */}
      <svg
        viewBox="0 0 200 380"
        className="w-full max-w-[190px] mx-auto block select-none"
        aria-label={`Body map — ${view} view`}
      >
        {zones.map((zone) => {
          const on = selected.has(zone.id);
          const cx = zone.x + zone.w / 2;
          const cy = zone.y + zone.h / 2;
          return (
            <g
              key={zone.id}
              role={readOnly ? undefined : "button"}
              aria-pressed={on}
              aria-label={zone.label}
              onClick={() => toggle(zone.id)}
              style={{ cursor: readOnly ? "default" : "pointer" }}
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
                  fontFamily: "system-ui, sans-serif",
                }}
              >
                {zone.svgLabel}
              </text>
            </g>
          );
        })}
      </svg>

      {/* Selected zones summary */}
      {markedLabels.length > 0 ? (
        <div className="flex flex-wrap gap-1">
          {markedLabels.map((label) => (
            <span
              key={label}
              className="text-xs bg-primary/10 text-primary px-2 py-0.5 rounded-full"
            >
              {label}
            </span>
          ))}
        </div>
      ) : (
        <p className="text-xs text-muted-foreground text-center">
          {readOnly ? "No body areas recorded." : "Click a zone to mark it."}
        </p>
      )}
    </div>
  );
}
