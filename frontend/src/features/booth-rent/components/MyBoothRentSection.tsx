import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useStudioCurrency } from "@/shared/hooks/useStudioCurrency";
import { useGetBoothRentSchedulesQuery, useGetBoothRentChargesQuery } from "../boothRentApi";
import { Money } from "@/shared/components/Money";
import { formatDateOnly } from "@/shared/utils/studioDay";

// Booth-rent dates are DATE-ONLY: saved as UTC midnight from a date input and advanced by whole days or
// months, so they are printed as the date itself. Neither the viewer's zone nor the studio's may move them;
// west of UTC either would print the day before. See "Booth-rent dates are date-only" in architecture.md.
function formatDate(iso: string): string {
  return formatDateOnly(iso, { day: "numeric", month: "short", year: "numeric" });
}

/** Artist's own read-only booth-rent schedule + charge history — embedded on /earnings, next to
 * the existing earnings report (the natural home for this, per the backlog spec). Self-scoping
 * (only the caller's own rows) is enforced server-side by GetBoothRentSchedulesQuery/
 * GetBoothRentChargesQuery, not by this component. */
export function MyBoothRentSection() {
  const { data: schedules, isLoading: schedulesLoading } = useGetBoothRentSchedulesQuery();
  const { data: charges, isLoading: chargesLoading } = useGetBoothRentChargesQuery();
  const { currency } = useStudioCurrency();

  if (schedulesLoading || chargesLoading) return <Skeleton className="h-24 w-full rounded-lg" />;
  if (!schedules || schedules.length === 0) return null;

  return (
    <section className="space-y-3">
      <h2 className="text-sm font-semibold text-muted-foreground">Booth Rent</h2>
      {schedules.map((s) => (
        <p key={s.id} className="text-sm text-muted-foreground">
          {currency ? <Money amount={s.amountFixed} currency={currency} /> : s.amountFixed} / {s.frequency.toLowerCase()} — next charge {formatDate(s.nextChargeDate)}
        </p>
      ))}
      {charges && charges.length > 0 && (
        <div className="space-y-1.5">
          {charges.map((c) => (
            <div key={c.id} className="flex items-center justify-between rounded-lg border p-2.5">
              <span className="text-sm"><Money amount={c.amount} currency={c.currency} /> — {formatDate(c.chargedDate)}</span>
              <Badge
                variant="outline"
                className={c.isSettled
                  ? "border-green-300 bg-green-100 text-green-800"
                  : "border-yellow-300 bg-yellow-100 text-yellow-800"}
              >
                {c.isSettled ? "Settled" : "Outstanding"}
              </Badge>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
