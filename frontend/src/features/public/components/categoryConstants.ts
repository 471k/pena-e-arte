// Split out from CategoryTabs.tsx because that file's component export trips
// react-refresh/only-export-components when mixed with this non-component export
// (see conductReportFormat.ts for the same precedent in this codebase).

// Keep in sync with PortfolioImageCategory.cs constants on the backend.
export const CATEGORIES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "",       label: "All"            },
  { value: "fresh",  label: "Fresh Tattoos"  },
  { value: "healed", label: "Healed Tattoos" },
  { value: "design", label: "Designs"        },
];
