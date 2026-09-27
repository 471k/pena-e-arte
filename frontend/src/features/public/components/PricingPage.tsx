import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetPublicPlansQuery } from "../publicApi";
import type { PublicPlanResponse } from "../publicApi";
import { planHighlights } from "../planHighlights";
import { ROUTE_META } from "@/shared/seo/siteRoutes";
import { formatCurrency } from "@/shared/utils/formatCurrency";

const INTERVAL_UNIT: Readonly<Record<string, string>> = { Monthly: "month", Yearly: "year" };

function PlanCard({ plan }: { plan: PublicPlanResponse }) {
  const monthly = plan.prices.find((p) => p.interval === "Monthly");
  const yearly = plan.prices.find((p) => p.interval === "Yearly");
  const headline = monthly ?? plan.prices[0];
  // Floored so the page never overstates the saving.
  const monthsFree = plan.yearlyMonthsFree === null ? 0 : Math.floor(plan.yearlyMonthsFree);

  return (
    <section className="rounded-lg border p-6">
      <h2 className="text-lg font-semibold">{plan.name}</h2>
      {headline && (
        <p className="mt-1 text-2xl font-semibold">
          {formatCurrency(headline.price, plan.currency)}
          <span className="text-sm font-normal text-muted-foreground">
            {" "}/ {INTERVAL_UNIT[headline.interval] ?? headline.interval.toLowerCase()}
          </span>
        </p>
      )}
      {monthly && yearly && (
        <p className="mt-1 text-sm text-muted-foreground">
          or {formatCurrency(yearly.price, plan.currency)} / year
          {monthsFree >= 1 && ` — ${monthsFree} ${monthsFree === 1 ? "month" : "months"} free`}
        </p>
      )}
      <ul className="mt-4 space-y-1.5 text-sm text-muted-foreground">
        {planHighlights(plan).map((highlight) => (
          <li key={highlight}>• {highlight}</li>
        ))}
      </ul>
    </section>
  );
}

export function PricingPage() {
  const { data: plans, isLoading, isError } = useGetPublicPlansQuery();

  return (
    <PublicContentLayout
      title={ROUTE_META["/pricing"].title}
      description={ROUTE_META["/pricing"].description}
      canonicalPath="/pricing"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Pricing</h1>
      <p className="mt-3 text-muted-foreground">
        Pick a plan for your studio. There is no commission on your bookings: clients pay
        the studio, not TattooOS.
      </p>

      {isLoading && (
        <div className="mt-8 grid gap-6 sm:grid-cols-2" aria-label="Loading pricing" aria-busy="true">
          <Skeleton className="h-64 rounded-lg" />
          <Skeleton className="h-64 rounded-lg" />
        </div>
      )}

      {isError && (
        <p className="mt-8 text-sm text-muted-foreground">
          Pricing is temporarily unavailable. Please try again shortly, or{" "}
          <Link to="/contact" className="underline underline-offset-2">
            contact us
          </Link>
          .
        </p>
      )}

      {!isLoading && !isError && plans && plans.length === 0 && (
        <p className="mt-8 text-sm text-muted-foreground">
          Pricing is being finalized.{" "}
          <Link to="/contact" className="underline underline-offset-2">
            Get in touch
          </Link>{" "}
          for current rates.
        </p>
      )}

      {!isLoading && !isError && plans && plans.length > 0 && (
        <div className="mt-8 grid gap-6 sm:grid-cols-2">
          {plans.map((plan) => (
            <PlanCard key={plan.name} plan={plan} />
          ))}
        </div>
      )}

      <MarketingCtaRow showPricing={false} />
    </PublicContentLayout>
  );
}
