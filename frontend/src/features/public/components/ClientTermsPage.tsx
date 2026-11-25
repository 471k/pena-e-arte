import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { LawyerReviewBanner } from "./LawyerReviewBanner";
import { ROUTE_META } from "@/shared/seo/siteRoutes";
import { LegalClauses, LegalVersionLine } from "../legal/LegalClauses";
import { CLIENT_TERMS_CLAUSES } from "../legal/clientTermsContent";
import { LEGAL_VERSION, LEGAL_LAST_UPDATED } from "../legal/legalMeta";

// Client Terms of Use: booking through a studio. Text: legal/clientTermsContent.ts.
export function ClientTermsPage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/client-terms"].title}
      description={ROUTE_META["/client-terms"].description}
      canonicalPath="/client-terms"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Client Terms of Use</h1>
      <LegalVersionLine lastUpdated={LEGAL_LAST_UPDATED} version={LEGAL_VERSION} />

      <div className="mt-6">
        <LawyerReviewBanner doc="clientTerms" />
      </div>

      <LegalClauses clauses={CLIENT_TERMS_CLAUSES} />

      <p className="mt-6 text-sm text-muted-foreground">
        See also the{" "}
        <Link to="/refund-policy" className="underline underline-offset-2 hover:text-foreground">
          Refund Policy
        </Link>{" "}
        and the{" "}
        <Link to="/privacy" className="underline underline-offset-2 hover:text-foreground">
          Privacy Policy
        </Link>
        .
      </p>
    </PublicContentLayout>
  );
}
