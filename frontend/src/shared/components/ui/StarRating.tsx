import { Star } from "lucide-react";

interface StarRatingProps {
  value:        number;
  max?:         number;
  interactive?: false;
  className?:   string;
}

interface InteractiveStarRatingProps {
  value:       number;
  max?:        number;
  interactive: true;
  onChange:    (rating: number) => void;
  className?:  string;
}

type Props = StarRatingProps | InteractiveStarRatingProps;

export function StarRating(props: Props) {
  const { value, max = 5, className = "" } = props;

  return (
    <div
      className={`flex gap-0.5 ${className}`}
      aria-label={`Rating: ${value} out of ${max}`}
      role={props.interactive ? "radiogroup" : "img"}
    >
      {Array.from({ length: max }, (_, i) => {
        const filled = i < value;
        if (props.interactive) {
          const rating = i + 1;
          return (
            <button
              key={i}
              type="button"
              aria-label={`Rate ${rating} out of ${max}`}
              aria-pressed={filled}
              onClick={() => props.onChange(rating)}
              className="focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-sm"
            >
              <Star
                className={`h-5 w-5 transition-colors ${
                  filled
                    ? "text-amber-400 fill-amber-400"
                    : "text-muted-foreground hover:text-amber-300"
                }`}
              />
            </button>
          );
        }

        return (
          <Star
            key={i}
            className={`h-3.5 w-3.5 ${
              filled ? "text-amber-400 fill-amber-400" : "text-muted-foreground/40"
            }`}
          />
        );
      })}
    </div>
  );
}
