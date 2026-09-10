/** Frontend mirror of Pena_e_Arte.Domain.Constants.TattooStyle.All — keep both in sync.
 *  Order/labels match PortfolioFeed.tsx's pre-existing STYLES list (minus its "All" filter
 *  sentinel, which is local to that component's own chip UI). */
export const TATTOO_STYLE_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "blackwork",       label: "Blackwork"       },
  { value: "realism",         label: "Realism"         },
  { value: "traditional",     label: "Traditional"     },
  { value: "geometric",       label: "Geometric"       },
  { value: "fineline",        label: "Fineline"        },
  { value: "watercolor",      label: "Watercolor"      },
  { value: "neo-traditional", label: "Neo-Traditional" },
  { value: "japanese",        label: "Japanese"        },
];
