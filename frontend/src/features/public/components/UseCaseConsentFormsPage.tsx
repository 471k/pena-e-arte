import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";
import { ROUTE_META } from "@/shared/seo/siteRoutes";

// Marketing page /use/consent-forms. Copy matches the ConsentForm entity: a signature,
// a signed-at timestamp and a snapshot of the exact consent text, plus PDF export.
export function UseCaseConsentFormsPage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/use/consent-forms"].title}
      description={ROUTE_META["/use/consent-forms"].description}
      canonicalPath="/use/consent-forms"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Digital consent forms</h1>

      <p className="mt-4 text-muted-foreground">
        Clients receive your studio&apos;s intake and consent forms before the appointment and
        sign them on any device. Nothing to print and nothing to lose.
      </p>
      <p className="mt-4 text-muted-foreground">
        Every signed form is timestamped and keeps the exact wording the client agreed to, so
        later edits to a template never change what was signed.
      </p>
      <p className="mt-4 text-muted-foreground">
        Signed forms sit on the client&apos;s record next to the appointment, and can be
        exported as a PDF when you need a copy.
      </p>

      <MarketingCtaRow />
    </PublicContentLayout>
  );
}
