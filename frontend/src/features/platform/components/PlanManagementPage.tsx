import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { CreditCard, Edit2, Loader2, Plus, Trash2, Users } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetIssuerPlansQuery, useDeletePlanMutation } from "@/features/billing/billingApi";
import { priceFor, type PlanResponse } from "@/features/billing/billing.types";

// null = unlimited on the plan
function formatLimit(value: number | null, unit: string): string {
  return value === null ? `Unlimited ${unit}` : `${value} ${unit}`;
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:                 "currency",
    currency:              "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}

function PlanCardSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-2 flex-1">
            <Skeleton className="h-5 w-24" />
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-5 w-28 rounded-full" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-6 w-6" />
            <Skeleton className="h-7 w-7 rounded" />
            <Skeleton className="h-7 w-7 rounded" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function PlanCard({ plan }: { plan: PlanResponse }) {
  const [deleting, setDeleting] = useState(false);
  const [deletePlan, { isLoading: removing }] = useDeletePlanMutation();

  const monthly = priceFor(plan, "Monthly");
  const yearly  = priceFor(plan, "Yearly");

  async function handleDelete() {
    try {
      await deletePlan(plan.id).unwrap();
      toast.success("Plan deleted");
    } catch (err: unknown) {
      const message =
        (err as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to delete plan.";
      toast.error(message);
    }
  }

  const isFree = monthly?.price === 0 || yearly?.price === 0;

  return (
    <Card className="hover:border-border/60 transition-colors">
      <CardContent className="p-4 space-y-3">
        {/* ── Info row ─────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          {/* left: info */}
          <div className="space-y-1 min-w-0">
            <p className="text-base font-semibold truncate" title={plan.name}>{plan.name}</p>
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="text-xs text-muted-foreground">
                {isFree
                  ? "Free forever"
                  : monthly && yearly ? "Monthly & yearly" : monthly ? "Billed monthly" : "Billed yearly only"}
              </span>
              {plan.allowBrandingRemoval && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300">
                  White-label
                </span>
              )}
              {plan.allowApiAccess && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300">
                  API access
                </span>
              )}
              {plan.prioritySupport && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300">
                  Priority support
                </span>
              )}
            </div>
            <p className="text-[11px] text-muted-foreground/70">
              {formatLimit(plan.maxArtists, "artists")} · {formatLimit(plan.maxAppointmentsPerMonth, "appts/mo")} ·{" "}
              {formatLimit(plan.maxStorageGb, "GB")}
            </p>
            <div className="space-y-0.5 mt-1">
              {isFree ? (
                <p className="text-sm font-mono font-medium text-emerald-600 dark:text-emerald-400">Free</p>
              ) : (
                <>
                  {monthly && (
                    <p className="text-sm font-mono">
                      <span className="font-medium">{formatCurrency(monthly.price)}</span>
                      <span className="text-xs text-muted-foreground">/mo</span>
                    </p>
                  )}
                  {yearly && (
                    <p className="text-sm font-mono">
                      <span className="font-medium">{formatCurrency(yearly.price)}</span>
                      <span className="text-xs text-muted-foreground">/yr</span>
                    </p>
                  )}
                </>
              )}
              {monthly && yearly && monthly.price > 0 && (
                <span className="inline-flex items-center text-xs px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-600 dark:bg-emerald-900/20 dark:text-emerald-400 font-medium">
                  Save {Math.round((1 - yearly.price / (monthly.price * 12)) * 100)}% annually
                </span>
              )}
            </div>
          </div>

          {/* right: subscriber count + actions */}
          <div className="flex items-center gap-3 shrink-0">
            <span
              className="flex items-center gap-1 text-xs text-muted-foreground"
              title={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed`}
              aria-label={`${plan.subscriberCount} studio${plan.subscriberCount !== 1 ? "s" : ""} subscribed to ${plan.name}`}
            >
              <Users className="h-3.5 w-3.5" />
              {plan.subscriberCount}
            </span>

            {!deleting && (
              <>
                <Button
                  asChild
                  size="sm"
                  variant="ghost"
                  className="h-8 w-8 p-0 text-muted-foreground hover:text-foreground transition-colors"
                  title="Edit"
                >
                  <Link to={`/platform/plans/${plan.id}/edit`} aria-label={`Edit ${plan.name} plan`}>
                    <Edit2 className="h-3.5 w-3.5" />
                  </Link>
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-8 w-8 p-0 text-red-500 dark:text-red-400 hover:text-red-600 dark:hover:text-red-300 hover:bg-red-50 dark:hover:bg-red-900/20 transition-colors"
                  onClick={() => setDeleting(true)}
                  aria-label={`Delete ${plan.name} plan`}
                  title="Delete"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </Button>
              </>
            )}
          </div>
        </div>

        {/* ── Delete confirmation ──────────────── */}
        {deleting && (
          <div className="border-t pt-3 space-y-2">
            {plan.subscriberCount > 0 ? (
              <p className="text-xs text-amber-600 dark:text-amber-400">
                <strong>{plan.subscriberCount} studio{plan.subscriberCount !== 1 ? "s" : ""}</strong>{" "}
                {plan.subscriberCount === 1 ? "is" : "are"} on this plan.
                Deleting it will prevent new signups — existing subscribers are not affected.
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">No active subscribers. Safe to delete.</p>
            )}
            <div className="flex items-center gap-2">
              <span className="text-xs text-destructive font-medium">
                Delete &quot;{plan.name}&quot; permanently?
              </span>
              <Button
                size="sm"
                variant="destructive"
                className="h-7 px-2 text-xs"
                disabled={removing}
                onClick={handleDelete}
              >
                {removing ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setDeleting(false)}
              >
                Cancel
              </Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function PlanManagementPage() {
  useDocumentMeta({ title: "Plans — Platform Admin", canonical: "/platform/plans" });

  const { data: plans, isLoading, isError } = useGetIssuerPlansQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Plans</span>
        </div>
        <Button asChild size="sm" className="gap-1.5">
          <Link to="/platform/plans/new">
            <Plus className="h-4 w-4" />
            New plan
          </Link>
        </Button>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6">
        {isLoading && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            <PlanCardSkeleton />
            <PlanCardSkeleton />
            <PlanCardSkeleton />
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load plans.</p>
        )}

        {!isLoading && !isError && plans?.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <CreditCard className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">No plans yet.</p>
            <p className="text-xs text-muted-foreground max-w-xs text-center">
              Create your first plan to allow studios to subscribe.
            </p>
            <Button asChild size="sm" variant="outline" className="gap-1.5 mt-2">
              <Link to="/platform/plans/new">
                <Plus className="h-4 w-4" />
                Create first plan
              </Link>
            </Button>
          </div>
        )}

        {!isLoading && !isError && plans && plans.length > 0 && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {plans.map((p) => (
              <PlanCard key={p.id} plan={p} />
            ))}

            {/* Ghost tile — always last; CSS grid handles wrapping at every breakpoint */}
            <Link
              to="/platform/plans/new"
              className="flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-border/40 p-8 text-muted-foreground/40 hover:border-border/70 hover:text-muted-foreground/60 transition-colors cursor-pointer min-h-[100px]"
              aria-label="Add a new plan"
            >
              <Plus className="h-5 w-5" />
              <span className="text-xs">New plan</span>
            </Link>
          </div>
        )}
      </main>
    </div>
  );
}
