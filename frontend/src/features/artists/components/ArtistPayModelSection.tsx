import { useId, useState } from "react";
import { ChevronDown, Loader2, RotateCw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { formatCurrency } from "@/shared/utils/formatCurrency";
import { useEditBuffer } from "@/shared/hooks/useEditBuffer";
import { useGetStudioPayModelQuery } from "@/features/studios/studiosApi";
import {
  ARRANGEMENT_LABEL,
  ARRANGEMENT_ORDER,
  formatRate,
  isArrangement,
  parseFee,
  parseRate,
  SELECT_CLS,
  type ArtistPayArrangement,
} from "@/features/studios/payModel";
import { useGetArtistPayModelQuery, useUpdateArtistPayModelMutation, type ArtistPayModel } from "../artistsApi";

/**
 * One artist's own pay terms (A07a), owner only. Each field left empty uses the studio's terms, so an
 * owner can give one artist a different rate without changing their arrangement. Collapsed by default
 * with a hand-rolled `hidden` disclosure (E04c's pattern), so the text stays in the page for find-in-page.
 * Saved through its own owner-only endpoint, never through the artist profile form, which the artist
 * can submit themselves. Only the value the effective arrangement uses is sent; the other goes as null so
 * no hidden rate or fee is stored under an arrangement that ignores it.
 */

const STUDIO_DEFAULT = "";

interface FormState {
  arrangement: ArtistPayArrangement | typeof STUDIO_DEFAULT;
  rate:        string;
  fee:         string;
}

function toForm(data: ArtistPayModel): FormState {
  return {
    arrangement: data.arrangement !== null && isArrangement(data.arrangement) ? data.arrangement : STUDIO_DEFAULT,
    rate:        data.commissionRate === null ? "" : String(data.commissionRate),
    fee:         data.flatFeePerSession === null ? "" : String(data.flatFeePerSession),
  };
}

export function ArtistPayModelSection({ artistId }: { artistId: string }) {
  const [open, setOpen] = useState(false);
  const { currentData, isLoading, isError, refetch, isFetching } = useGetArtistPayModelQuery(artistId, { skip: !open });
  const { data: studioTerms } = useGetStudioPayModelQuery(undefined, { skip: !open });
  const [save, { isLoading: saving }] = useUpdateArtistPayModelMutation();
  // Filled once per artist (and again after collapsing), never on a refetch, so typing survives one.
  const [form, setForm, resetForm] = useEditBuffer<ArtistPayModel, FormState>(
    currentData, currentData ? artistId : undefined, toForm);
  const ids = { region: useId(), arrangement: useId(), rate: useId(), fee: useId() };

  const rate = parseRate(form?.rate ?? "");
  const fee = parseFee(form?.fee ?? "");
  const studioArrangement = studioTerms && isArrangement(studioTerms.arrangement) ? studioTerms.arrangement : null;
  const effective = form?.arrangement || studioArrangement;
  const studioDefaultLabel = studioArrangement ? `Studio's terms (${ARRANGEMENT_LABEL[studioArrangement]})` : "Studio's terms";
  const studioRate = studioTerms?.commissionRate ?? null;
  const studioFee = studioTerms?.flatFeePerSession ?? null;
  // Which value the effective arrangement reads. While the studio's terms haven't loaded, `effective` is
  // unknown and both fields are hidden: send them as loaded and let the server drop the unused one.
  const usesRate = effective === null || effective === "Commission";
  const usesFee = effective === null || effective === "FlatFeePerSession";

  async function onSave() {
    if (!form || (usesRate && rate.error) || (usesFee && fee.error)) return;
    try {
      const saved = await save({
        artistId,
        body: {
          arrangement:       form.arrangement === STUDIO_DEFAULT ? null : form.arrangement,
          commissionRate:    usesRate ? rate.value : null,
          flatFeePerSession: usesFee ? fee.value : null,
        },
      }).unwrap();
      resetForm(saved);
      toast.success("Pay terms saved.");
    } catch {
      toast.error("Couldn't save this artist's pay terms. Please try again.");
    }
  }

  return (
    <Card data-testid="artist-pay-model">
      <CardContent className="p-4 space-y-3">
        <button
          type="button"
          className="flex w-full items-center justify-between text-sm font-medium"
          aria-expanded={open}
          aria-controls={ids.region}
          onClick={() => setOpen((v) => !v)}
        >
          How this artist gets paid
          <ChevronDown className={cn("h-4 w-4 transition-transform", open && "rotate-180")} aria-hidden="true" />
        </button>

        <div id={ids.region} hidden={!open} className="space-y-3">
          <p className="text-xs text-muted-foreground">
            Only you can see or change this. Leave a field empty to use the studio's terms. It decides the
            share this artist sees on My Earnings; nothing is paid out automatically.
          </p>

          {isLoading && <Skeleton className="h-10 w-full" data-testid="artist-pay-model-loading" />}

          {isError && (
            <div className="flex items-center justify-between gap-3">
              <p className="text-sm text-destructive-text" role="alert">We couldn't load this artist's pay terms.</p>
              <Button size="sm" variant="outline" className="h-7 text-xs gap-1" disabled={isFetching} onClick={() => void refetch()}>
                <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
                Try again
              </Button>
            </div>
          )}

          {currentData && form && (
            <>
              <div className="space-y-1.5">
                <Label htmlFor={ids.arrangement}>Arrangement</Label>
                <select
                  id={ids.arrangement}
                  className={SELECT_CLS}
                  value={form.arrangement}
                  onChange={(e) => {
                    const next = e.target.value;
                    setForm({ ...form, arrangement: next === STUDIO_DEFAULT ? STUDIO_DEFAULT : isArrangement(next) ? next : form.arrangement });
                  }}
                >
                  <option value={STUDIO_DEFAULT}>{studioDefaultLabel}</option>
                  {ARRANGEMENT_ORDER.map((a) => <option key={a} value={a}>{ARRANGEMENT_LABEL[a]}</option>)}
                </select>
              </div>

              {effective === "Commission" && (
                <div className="space-y-1.5">
                  <Label htmlFor={ids.rate}>This artist's share (%)</Label>
                  <Input
                    id={ids.rate}
                    inputMode="decimal"
                    className="w-28"
                    placeholder={studioRate === null ? "Not set" : formatRate(studioRate)}
                    value={form.rate}
                    aria-invalid={rate.error !== null}
                    onChange={(e) => setForm({ ...form, rate: e.target.value })}
                  />
                  <p className="text-xs text-muted-foreground">
                    {form.rate === ""
                      ? studioRate === null
                        ? "Empty uses the studio's rate, which isn't set yet."
                        : `Empty uses the studio's rate, ${formatRate(studioRate)}.`
                      : "Overrides the studio's rate for this artist."}
                  </p>
                  {rate.error && <p className="text-xs text-destructive-text">{rate.error}</p>}
                </div>
              )}

              {effective === "FlatFeePerSession" && (
                <div className="space-y-1.5">
                  <Label htmlFor={ids.fee}>The studio's fee per session for this artist{studioTerms ? ` (${studioTerms.currency})` : ""}</Label>
                  <Input
                    id={ids.fee}
                    inputMode="decimal"
                    className="w-40"
                    placeholder={studioFee === null || !studioTerms ? "Not set" : formatCurrency(studioFee, studioTerms.currency)}
                    value={form.fee}
                    aria-invalid={fee.error !== null}
                    onChange={(e) => setForm({ ...form, fee: e.target.value })}
                  />
                  {fee.error && <p className="text-xs text-destructive-text">{fee.error}</p>}
                </div>
              )}

              <Button size="sm" onClick={() => void onSave()} disabled={saving} className="gap-2">
                {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
                Save pay terms
              </Button>
            </>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
