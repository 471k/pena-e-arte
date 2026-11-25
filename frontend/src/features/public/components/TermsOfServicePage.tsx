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
      <LegalVersionLine lastUpdated={LEGAL_LAST_UPDATED} version={LEGAL_VERSION} />

      <div className="mt-6">
        <LawyerReviewBanner doc="terms" />
      </div>

      <LegalClauses clauses={PLATFORM_TERMS_CLAUSES} />
      <LegalClauses clauses={DATA_PROCESSING_CLAUSES} />
    </PublicContentLayout>
  );
}
