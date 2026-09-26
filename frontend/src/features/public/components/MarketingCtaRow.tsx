import { Link } from "react-router-dom";

const PRIMARY_LINK =
  "rounded-md bg-violet-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-violet-700";
const SECONDARY_LINK =
  "rounded-md border-2 border-violet-500 bg-violet-500/5 px-4 py-2 text-sm font-medium text-violet-700 dark:text-violet-400 transition-colors hover:bg-violet-500/15 hover:text-violet-800 dark:hover:text-violet-300";

interface MarketingCtaRowProps {
  /** Hide the pricing link on the pricing page itself. */
  showPricing?: boolean;
}

// Closing call-to-action row shared by the marketing pages (Features, Pricing, use-case
// pages). Same two buttons the Home page uses, so the funnel reads the same everywhere.
export function MarketingCtaRow({ showPricing = true }: MarketingCtaRowProps) {
  return (
    <div className="mt-10 flex flex-wrap gap-3">
      {showPricing && (
        <Link to="/pricing" className={PRIMARY_LINK}>
          See pricing
        </Link>
      )}
      <Link to="/register" className={showPricing ? SECONDARY_LINK : PRIMARY_LINK}>
        Register your studio
      </Link>
    </div>
  );
}
