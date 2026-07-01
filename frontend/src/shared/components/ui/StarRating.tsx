import { Star } from "lucide-react";
import { useState } from "react";

// "sm" = display-only star (review cards, tile overlays)
// "md" = interactive stars in the write form
const SIZE = {
  sm: "h-3.5 w-3.5",
  md: "h-5 w-5",
} as const;

// ── Display-only (read) star rating ─────────────────────────────────────────

interface DisplayStarRatingProps {
  value:      number;
  max?:       number;
  size?:      keyof typeof SIZE;
  className?: string;
}

export function StarRating({
  value,
  max = 5,
  size = "sm",
  className = "",
}: DisplayStarRatingProps) {
  return (
    <div
      className={`flex gap-0.5 ${className}`}
      role="img"
      aria-label={`Rating: ${value} out of ${max} stars`}
    >
      {Array.from({ length: max }, (_, i) => (
        <Star
          key={i}
          aria-hidden="true"
          className={`${SIZE[size]} shrink-0 ${
            i < value
              ? "text-amber-400 fill-amber-400"
              : "text-muted-foreground/40 fill-none"
          }`}
        />
      ))}
    </div>
  );
}

// ── Interactive (write) star rating ─────────────────────────────────────────

const LABELS = ["Terrible", "Poor", "Okay", "Good", "Excellent"] as const;

interface InteractiveStarRatingProps {
  value:      number;
  max?:       number;
  onChange:   (rating: number) => void;
  className?: string;
}

export function InteractiveStarRating({
  value,
  max = 5,
  onChange,
  className = "",
}: InteractiveStarRatingProps) {
  const [hovered, setHovered] = useState(0);

  const highlighted = hovered > 0 ? hovered : value;

  return (
    <div className={`space-y-1 ${className}`}>
      <div
        role="radiogroup"
        aria-label="Star rating"
        className="flex gap-0.5"
        onMouseLeave={() => setHovered(0)}
      >
        {Array.from({ length: max }, (_, i) => {
          const rating = i + 1;
          const isHighlighted = rating <= highlighted;

          return (
            <button
              key={i}
              type="button"
              role="radio"
              aria-checked={value === rating}
              aria-label={`Rate ${rating} of ${max} — ${LABELS[i]}`}
              onClick={() => onChange(rating)}
              onMouseEnter={() => setHovered(rating)}
              onFocus={() => setHovered(rating)}
              onBlur={() => setHovered(0)}
              className={`
                min-w-[44px] min-h-[44px]
                flex items-center justify-center
                rounded-sm
                focus:outline-none focus-visible:ring-2 focus-visible:ring-ring
                transition-transform duration-75
                ${isHighlighted ? "scale-110" : "scale-100"}
              `}
            >
              <Star
                aria-hidden="true"
                className={`${SIZE.md} transition-colors duration-100 ${
                  isHighlighted
                    ? "text-amber-400 fill-amber-400"
                    : "text-muted-foreground/50 fill-none"
                }`}
              />
            </button>
          );
        })}
      </div>

      {/* Live text readout — visible below the stars after selection */}
      <p
        aria-live="polite"
        aria-atomic="true"
        className="h-4 text-xs text-muted-foreground transition-opacity duration-150"
        style={{ opacity: value > 0 ? 1 : 0 }}
      >
        {value > 0 ? `${value} star${value !== 1 ? "s" : ""} — ${LABELS[value - 1]}` : ""}
      </p>
    </div>
  );
}
