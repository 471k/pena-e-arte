// Shared segmented category filter — used by both PortfolioFeed.tsx (public Discover feed)
// and ArtistPortfolioPage.tsx (an artist's own public portfolio page).

// Keep in sync with PortfolioImageCategory.cs constants on the backend.
export const CATEGORIES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "",       label: "All"            },
  { value: "fresh",  label: "Fresh Tattoos"  },
  { value: "healed", label: "Healed Tattoos" },
  { value: "design", label: "Designs"        },
];

interface CategoryTabsProps {
  activeCategory: string;
  onChange:       (category: string) => void;
  categories?:    ReadonlyArray<{ value: string; label: string }>;
  className?:     string;
}

export function CategoryTabs({
  activeCategory, onChange, categories = CATEGORIES, className = "",
}: CategoryTabsProps) {
  return (
    <div
      role="group"
      aria-label="Filter by portfolio category"
      className={`flex items-center gap-1 rounded-lg border border-border bg-muted/40 p-1 w-fit ${className}`}
    >
      {categories.map(({ value, label }) => {
        const isActive = activeCategory === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={isActive}
            onClick={() => onChange(value)}
            className={`px-3 py-1.5 min-h-[36px] rounded-md text-xs font-medium
                        transition-colors whitespace-nowrap
                        ${isActive
                          ? "bg-background text-foreground shadow-sm"
                          : "text-muted-foreground hover:text-foreground"
                        }`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}
