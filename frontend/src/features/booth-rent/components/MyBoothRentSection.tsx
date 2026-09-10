import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetBoothRentSchedulesQuery, useGetBoothRentChargesQuery } from "../boothRentApi";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

/** Artist's own read-only booth-rent schedule + charge history — embedded on /earnings, next to
 * the existing earnings report (the natural home for this, per the backlog spec). Self-scoping
 * (only the caller's own rows) is enforced server-side by GetBoothRentSchedulesQuery/
 * GetBoothRentChargesQuery, not by this component. */
export function MyBoothRentSection() {
  const { data: schedules, isLoading: schedulesLoading } = useGetBoothRentSchedulesQuery();
  const { data: charges, isLoading: chargesLoading } = useGetBoothRentChargesQuery();

  if (schedulesLoading || chargesLoading) return <Skeleton className="h-24 w-full rounded-lg" />;
  if (!schedules || schedules.length === 0) return null;

  return (
    <section className="space-y-3">
      <h2 className="text-sm font-semibold text-muted-foreground">Booth Rent</h2>
      {schedules.map((s) => (
        <p key={s.id} className="text-sm text-muted-foreground">
          {formatCurrency(s.amountFixed)} / {s.frequency.toLowerCase()} — next charge {formatDate(s.nextChargeDate)}
        </p>
      ))}
      {charges && charges.length > 0 && (
        <div className="space-y-1.5">
          {charges.map((c) => (
            <div key={c.id} className="flex items-center justify-between rounded-lg border p-2.5">
              <span className="text-sm">{formatCurrency(c.amount)} — {formatDate(c.chargedDate)}</span>
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
