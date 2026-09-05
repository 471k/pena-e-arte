import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { SITE_TAGLINE, SITE_META_DESCRIPTION } from "@/shared/constants/legalEntity";

// First-touch public landing surface for unauthenticated root visits. Replaces the
// previous behaviour of bouncing every guest at "/" straight into /discover.
export function HomePage() {
  return (
    <PublicContentLayout title={SITE_TAGLINE} description={SITE_META_DESCRIPTION} canonicalPath="/">
      <h1 className="text-3xl font-semibold tracking-tight">TattooOS</h1>
      <p className="mt-3 text-muted-foreground">
        Booking, deposits, digital consent forms, design approvals, and studio management
        built for tattoo studios and their clients.
      </p>

      <div className="mt-6 flex flex-wrap gap-3">
        <Link
          to="/discover"
          className="rounded-md bg-violet-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-violet-700"
        >
          Discover studios
        </Link>
        <Link
          to="/register"
          className="rounded-md border-2 border-violet-500 bg-violet-500/5 px-4 py-2 text-sm font-medium text-violet-700 dark:text-violet-400 transition-colors hover:bg-violet-500/15 hover:text-violet-800 dark:hover:text-violet-300"
        >
          Register your studio
        </Link>
      </div>

      <nav aria-label="Policies" className="mt-10 flex flex-wrap gap-x-4 gap-y-2 text-sm text-muted-foreground">
        <Link to="/privacy" className="underline underline-offset-2 hover:text-foreground">
          Privacy Policy
        </Link>
        <Link to="/terms" className="underline underline-offset-2 hover:text-foreground">
          Terms of Service
        </Link>
        <Link to="/refund-policy" className="underline underline-offset-2 hover:text-foreground">
          Refund Policy
        </Link>
        <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
          Contact
        </Link>
      </nav>
    </PublicContentLayout>
  );
}
