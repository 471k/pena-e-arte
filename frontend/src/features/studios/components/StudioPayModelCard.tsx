import { useId, useState } from "react";
import { Check, ChevronDown, CircleDashed, Loader2, RotateCw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { useEditBuffer } from "@/shared/hooks/useEditBuffer";
import { useGetStudioPayModelQuery, useUpdateStudioPayModelMutation, type StudioPayModel } from "../studiosApi";
import {
  ARRANGEMENT_HINT,
  ARRANGEMENT_LABEL,
  ARRANGEMENT_ORDER,
  ArtistPayArrangement,
  formatRate,
  isArrangement,
  parseFee,
  parseRate,
  SELECT_CLS,
} from "../payModel";

/**
 * How the studio's artists are paid unless an artist has their own terms (DEC10, A07a). Every studio
 * starts on commission with no rate, deliberately: there is no standard percentage, so a seeded one would
 * read as chosen when nobody chose it. Until the owner saves, the setup checklist shows this as "set for
 * you, not yet reviewed" and commission artists see no share on My Earnings. Only the value the chosen
 * arrangement uses is sent; the other goes as null, so no hidden rate or fee is stored.
 */

interface FormState {
  arrangement: ArtistPayArrangement;
  rate:        string; // percent, as typed
  fee:         string; // studio currency, as typed
}

function toForm(data: StudioPayModel): FormState {
  return {
    arrangement: isArrangement(data.arrangement) ? data.arrangement : ArtistPayArrangement.Commission,
    rate:        data.commissionRate === null ? "" : String(data.commissionRate),
    fee:         data.flatFeePerSession === null ? "" : String(data.flatFeePerSession),
  };
}

function ReviewState({ reviewedAt }: { reviewedAt: string | null }) {
  if (reviewedAt) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-muted-foreground" data-testid="pay-model-state" data-state="Reviewed">
        <Check className="h-3.5 w-3.5 text-success-text" aria-hidden="true" />
        Saved {new Date(reviewedAt).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" })}
      </p>
    );
  }
  return (
    <p className="flex items-start gap-1.5 text-xs text-warning-text" data-testid="pay-model-state" data-state="DefaultUnreviewed">
      <CircleDashed className="h-3.5 w-3.5 mt-0.5 shrink-0" aria-hidden="true" />
      Set for you, not reviewed yet. Every studio starts on commission with no rate, so commission artists
      can't see their share until you set one.
    </p>
  );
}

export function StudioPayModelCard() {
  const { currentData: data, isLoading, isError, refetch, isFetching } = useGetStudioPayModelQuery();
  const [save, { isLoading: saving }] = useUpdateStudioPayModelMutation();
  // Filled on first load only, never on a refetch, so typing survives one (A07a cleanup, finding 2).
  const [form, setForm, resetForm] = useEditBuffer<StudioPayModel, FormState>(data, data ? "studio" : undefined, toForm);
  const [open, setOpen] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const ids = { arrangement: useId(), rate: useId(), fee: useId(), how: useId() };

  const rate = parseRate(form?.rate ?? "");
  const fee = parseFee(form?.fee ?? "");
  const needsRate = form?.arrangement === ArtistPayArrangement.Commission;
  const needsFee = form?.arrangement === ArtistPayArrangement.FlatFeePerSession;
  // A hidden field's text never blocks a save: it isn't sent.
  const rateError = needsRate ? rate.error ?? (submitted && rate.value === null ? "Set the commission rate." : null) : null;
  const feeError = needsFee ? fee.error ?? (submitted && fee.value === null ? "Set the fee per session." : null) : null;

  async function onSave() {
    if (!form) return;
    setSubmitted(true);
    if ((needsRate && (rate.error || rate.value === null)) || (needsFee && (fee.error || fee.value === null))) return;
    try {
      const saved = await save({
        arrangement:       form.arrangement,
        commissionRate:    needsRate ? rate.value : null,
        flatFeePerSession: needsFee ? fee.value : null,
      }).unwrap();
      resetForm(saved);
      setSubmitted(false);
      toast.success("Artist pay saved.");
    } catch {
      toast.error("Couldn't save how artists get paid. Please try again.");
    }
  }

  return (
    <Card data-tour="owner-studio-pay-model-card" data-testid="studio-pay-model-card">
      <CardHeader>
        <CardTitle className="text-base">How artists get paid</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Used for every artist unless you set their own terms on their profile. It decides the share each
          artist sees on My Earnings. Nothing is paid out automatically.
        </p>

        {isLoading && (
          <div className="space-y-2" data-testid="pay-model-loading">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-1/2" />
          </div>
        )}

        {isError && (
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-destructive-text" role="alert">We couldn't load how artists get paid.</p>
            <Button size="sm" variant="outline" className="h-7 text-xs gap-1" disabled={isFetching} onClick={() => void refetch()}>
              <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
              Try again
            </Button>
          </div>
        )}

        {data && form && (
          <>
            <ReviewState reviewedAt={data.reviewedAt} />

            <div className="space-y-1.5">
              <Label htmlFor={ids.arrangement}>Arrangement</Label>
              <select
                id={ids.arrangement}
                className={SELECT_CLS}
                value={form.arrangement}
                onChange={(e) => {
                  const next = e.target.value;
                  if (isArrangement(next)) setForm({ ...form, arrangement: next });
                }}
              >
                {ARRANGEMENT_ORDER.map((a) => <option key={a} value={a}>{ARRANGEMENT_LABEL[a]}</option>)}
              </select>
              <p className="text-xs text-muted-foreground">{ARRANGEMENT_HINT[form.arrangement]}</p>
            </div>

            {needsRate && (
              <div className="space-y-1.5">
                <Label htmlFor={ids.rate}>Artist's share (% of what the client paid)</Label>
                <div className="flex items-center gap-2">
                  <Input
                    id={ids.rate}
                    inputMode="decimal"
                    className="w-28"
                    placeholder="Not set"
                    value={form.rate}
                    aria-invalid={rateError !== null}
                    aria-describedby={rateError ? `${ids.rate}-error` : undefined}
                    onChange={(e) => setForm({ ...form, rate: e.target.value })}
                  />
                  <span className="text-sm text-muted-foreground">%</span>
                  {data.commissionRate === null && form.rate === "" && (
                    <span className="text-xs text-muted-foreground" data-testid="pay-model-rate-unset">No rate set yet</span>
                  )}
                  {rate.value !== null && !rateError && (
                    <span className="text-xs text-muted-foreground">The artist gets {formatRate(rate.value)}, the studio the rest.</span>
                  )}
                </div>
                {rateError && <p id={`${ids.rate}-error`} className="text-xs text-destructive-text">{rateError}</p>}
              </div>
            )}

            {needsFee && (
              <div className="space-y-1.5">
                <Label htmlFor={ids.fee}>The studio's fee per session ({data.currency})</Label>
                <Input
                  id={ids.fee}
                  inputMode="decimal"
                  className="w-40"
                  placeholder="Not set"
                  value={form.fee}
                  aria-invalid={feeError !== null}
                  aria-describedby={feeError ? `${ids.fee}-error` : undefined}
                  onChange={(e) => setForm({ ...form, fee: e.target.value })}
                />
                {feeError && <p id={`${ids.fee}-error`} className="text-xs text-destructive-text">{feeError}</p>}
              </div>
            )}

            <div>
              <button
                type="button"
                className="flex items-center gap-1 text-xs text-link underline underline-offset-2"
                aria-expanded={open}
                aria-controls={ids.how}
                onClick={() => setOpen((v) => !v)}
              >
                How shares are worked out
                <ChevronDown className={cn("h-3 w-3 transition-transform", open && "rotate-180")} aria-hidden="true" />
              </button>
              {/* `hidden` while collapsed (E04c's pattern), not an Accordion that unmounts the text. */}
              <div id={ids.how} hidden={!open} className="mt-2 space-y-1 text-xs text-muted-foreground">
                <p>Shares are worked out on what the client paid, minus any refund, before the card fee.</p>
                <p>An artist's own terms, set on their profile, come first; anything they don't have uses these.</p>
                <p>Booth rent itself is charged under Booth rent; this only says the artist keeps what clients pay.</p>
              </div>
            </div>

            <Button onClick={() => void onSave()} disabled={saving} className="gap-2">
              {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
              Save artist pay
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
