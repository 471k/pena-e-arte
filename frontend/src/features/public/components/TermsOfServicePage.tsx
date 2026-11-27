import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { LawyerReviewBanner } from "./LawyerReviewBanner";
import { ROUTE_META } from "@/shared/seo/siteRoutes";
import { LegalClauses, LegalVersionLine } from "../legal/LegalClauses";
import { PLATFORM_TERMS_CLAUSES, DATA_PROCESSING_CLAUSES } from "../legal/platformTermsContent";
import { LEGAL_VERSION, LEGAL_LAST_UPDATED } from "../legal/legalMeta";

// Platform Terms for Studios. Text: legal/platformTermsContent.ts. The Client Terms of Use are at /client-terms.
export function TermsOfServicePage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/terms"].title}
      description={ROUTE_META["/terms"].description}
      canonicalPath="/terms"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Platform Terms for Studios</h1>
      {/* Audience signpost for people who arrive by direct link (old bookmark, search result),
          where relabelling the site's own links cannot reach them. Mirrored on ClientTermsPage. */}
      <p className="mt-2 text-sm text-muted-foreground">
        Not a studio? These terms do not apply to you — see the{" "}
        <Link to="/client-terms" className="underline underline-offset-2 hover:text-foreground">
          Client Terms of Use
        </Link>
        .
      </p>
      <LegalVersionLine lastUpdated={LEGAL_LAST_UPDATED} version={LEGAL_VERSION} />

      <div className="mt-6">
        <LawyerReviewBanner doc="terms" />
      </div>

      <LegalClauses clauses={PLATFORM_TERMS_CLAUSES} />
      <LegalClauses clauses={DATA_PROCESSING_CLAUSES} />
    </PublicContentLayout>
  );
}
