import { useState } from "react";
import { Loader2, Lock } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { CurrencySelect } from "@/shared/components/ui/currency-select";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { currencyDisplayName } from "@/shared/utils/currencies";
import { useGetMyStudioQuery, useUpdateMyStudioCurrencyMutation } from "../studiosApi";

/**
 * Studio Settings' Currency card. Separate from the main "Studio details" form/card because it
 * has its own server round trip, its own lock state (once payments have been recorded, the
 * currency can no longer change — see StudioCurrencyLock on the backend), and a confirmation
 * step the plain details form doesn't need.
 */
export function CurrencySettingsCard() {
  const { data: studio, isLoading, isError, refetch } = useGetMyStudioQuery();
  const [updateCurrency, { isLoading: saving }] = useUpdateMyStudioCurrencyMutation();

  // null means "no manual pick yet — follow the loaded studio.currency", so this never needs an
  // effect to sync from `studio` once it arrives (see feedback_react_state_sync_pattern).
  const [manualSelection, setManualSelection] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [error, setError]           = useState<string | null>(null);

  if (isLoading) {
    return (
      <Card data-tour="owner-studio-currency-card">
        <CardHeader>
          <CardTitle className="text-base">Currency</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <Skeleton className="h-4 w-56" />
          <Skeleton className="h-10 w-full rounded-md" />
        </CardContent>
      </Card>
    );
  }

  if (isError || !studio) {
    return (
      <Card data-tour="owner-studio-currency-card">
        <CardHeader>
          <CardTitle className="text-base">Currency</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <p className="text-sm text-destructive-text">Couldn't load currency settings.</p>
          <Button type="button" variant="outline" size="sm" onClick={() => refetch()}>
            Retry
          </Button>
        </CardContent>
      </Card>
    );
  }

  const selected = manualSelection ?? studio.currency;

  async function confirmChange() {
    try {
      await updateCurrency({ currency: selected }).unwrap();
      toast.success("Currency updated.");
      setError(null);
      setConfirmOpen(false);
      setManualSelection(null);
    } catch (err: unknown) {
      const msg =
        err && typeof err === "object" && "data" in err && err.data &&
        typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Unable to change currency.";
      setError(msg);
      setConfirmOpen(false);
    }
  }

  return (
    <Card data-tour="owner-studio-currency-card">
      <CardHeader>
        <CardTitle className="text-base">Currency</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {error && <p className="text-sm text-destructive-text">{error}</p>}

        {studio.currencyLocked ? (
          <div className="flex items-start gap-2">
            <Lock className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" aria-hidden="true" />
            <div className="space-y-1">
              <p className="text-sm font-medium">
                {currencyDisplayName(studio.currency)} ({studio.currency})
              </p>
              <p className="text-xs text-muted-foreground">
                Locked because payments have been recorded in this currency.{" "}
                <a
                  href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@tattooos.co"}`}
                  className="font-medium underline underline-offset-4"
                >
                  Contact support
                </a>{" "}
                to change it.
              </p>
            </div>
          </div>
        ) : (
          <>
            <CurrencySelect
              id="studio-currency"
              value={selected}
              onChange={setManualSelection}
              disabled={saving}
            />
            <p className="text-xs text-muted-foreground">
              Prices, deposits and payments in your studio use this currency.
            </p>
            <Button
              type="button"
              size="sm"
              disabled={saving || !selected || selected === studio.currency}
              onClick={() => setConfirmOpen(true)}
              className="gap-2"
            >
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              Save
            </Button>
          </>
        )}
      </CardContent>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Change currency to {selected}?</AlertDialogTitle>
            <AlertDialogDescription>
              Existing prices will be shown in {selected} without conversion — review your
              services, deposit rules, packages and gift-card amounts after changing.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              disabled={saving}
              onClick={(e) => { e.preventDefault(); void confirmChange(); }}
            >
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : "Change currency"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}
