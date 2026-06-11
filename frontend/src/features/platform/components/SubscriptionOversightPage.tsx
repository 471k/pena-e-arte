import { useState } from "react";
import { Loader2, Receipt } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
} from "@/features/platform/platformApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
};

interface SubscriptionRowProps {
  sub: PlatformSubscriptionResponse;
}

function SubscriptionRow({ sub }: SubscriptionRowProps) {
  const [extending, setExtending] = useState(false);
  const [days, setDays]           = useState("7");
  const [extendTrial, { isLoading }] = useExtendTrialMutation();

  async function handleExtend() {
    const additionalDays = parseInt(days, 10);
    if (isNaN(additionalDays) || additionalDays < 1) return;
    await extendTrial({ studioId: sub.studioId, additionalDays }).unwrap();
    setExtending(false);
  }

  const statusClass = STATUS_CLASSES[sub.status] ?? STATUS_CLASSES.NoSubscription;

  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-sm">{sub.studioName}</span>
              <span className="text-xs text-muted-foreground font-mono">{sub.studioSlug}</span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${statusClass}`}>
                {sub.status}
              </span>
            </div>
            <p className="text-xs text-muted-foreground">
              {sub.planName ?? "No plan"}
              {" · "}
              Trial ends {new Date(sub.trialExpiresAt).toLocaleDateString("en-GB")}
              {" · "}
              Period end {new Date(sub.currentPeriodEnd).toLocaleDateString("en-GB")}
            </p>
          </div>

          {sub.status === "Trialing" && !extending && (
            <Button size="sm" variant="outline" className="h-7 text-xs shrink-0"
              onClick={() => setExtending(true)}>
              Extend trial
            </Button>
          )}
        </div>

        {extending && (
          <div className="flex items-center gap-2 pt-1">
            <Input
              type="number"
              min="1"
              max="90"
              value={days}
              onChange={(e) => setDays(e.target.value)}
              className="h-7 w-20 text-xs"
            />
            <span className="text-xs text-muted-foreground">days</span>
            <Button size="sm" className="h-7 px-2 text-xs" disabled={isLoading} onClick={handleExtend}>
              {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
            </Button>
            <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
              onClick={() => setExtending(false)}>
              Cancel
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function SubscriptionOversightPage() {
  const { data: subscriptions, isLoading, isError } = useGetPlatformSubscriptionsQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Receipt className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Subscriptions</span>
        {subscriptions && (
          <span className="text-xs text-muted-foreground ml-1">({subscriptions.length})</span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load subscriptions.</p>
        )}

        {!isLoading && !isError && subscriptions?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No studios found.</p>
        )}

        {!isLoading && !isError && subscriptions?.map((sub) => (
          <SubscriptionRow key={sub.studioId} sub={sub} />
        ))}
      </main>
    </div>
  );
}
