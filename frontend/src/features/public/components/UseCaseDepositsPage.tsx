import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";
import { ROUTE_META } from "@/shared/seo/siteRoutes";

// Marketing page /use/deposits. Copy is limited to what is verified in the code: deposit
// rules (fixed amount or percentage, cancellation window, refund share on late cancel),
// zero platform commission (ADR-0001 Amendment B), cash confirmed by staff, and card
// deposits only where the studio has connected its own payment account
// (CardPaymentsAvailable). No provider is named.
export function UseCaseDepositsPage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/use/deposits"].title}
      description={ROUTE_META["/use/deposits"].description}
      canonicalPath="/use/deposits"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Deposits for tattoo bookings</h1>

      <p className="mt-4 text-muted-foreground">
        Each studio sets its own deposit rules: a fixed amount or a percentage, how late a
        client can cancel, and what share of the deposit is refunded after that point.
        Clients see what is due while they book.
      </p>
      <p className="mt-4 text-muted-foreground">
        Deposits go to your studio, not through TattooOS, and there is no commission on
        bookings. Cash deposits are marked as received by the artist or owner. Card deposits
        are paid into the studio&apos;s own payment account, for studios that have connected one.
      </p>
      <p className="mt-4 text-muted-foreground">
        If a client does not turn up, the deposit is forfeited. A late cancellation refunds the
        share your studio has configured.
      </p>

      <MarketingCtaRow />
    </PublicContentLayout>
  );
}
