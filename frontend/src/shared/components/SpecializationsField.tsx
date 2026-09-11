import { TATTOO_STYLE_OPTIONS } from "@/shared/constants/tattooStyles";

interface SpecializationsFieldProps {
  value: string[];
  onChange: (value: string[]) => void;
  id?: string;
}

/** Toggleable chip group over the canonical TattooStyle vocabulary — the structured
 *  replacement for the old freeform Specializations text field. */
export function SpecializationsField({ value, onChange, id }: SpecializationsFieldProps) {
  function toggle(style: string) {
    onChange(value.includes(style) ? value.filter((s) => s !== style) : [...value, style]);
  }

  return (
    <div id={id} role="group" aria-label="Specializations" className="flex flex-wrap gap-1.5">
      {TATTOO_STYLE_OPTIONS.map(({ value: styleValue, label }) => {
        const isActive = value.includes(styleValue);
        return (
          <button
            key={styleValue}
            type="button"
            role="checkbox"
            aria-checked={isActive}
            aria-label={label}
            onClick={() => toggle(styleValue)}
            className={`px-3 py-1.5 min-h-[36px] rounded-full text-xs font-medium border
                        transition-colors
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
