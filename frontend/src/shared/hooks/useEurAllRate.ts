import { useGetEurAllRateQuery } from "@/features/public/publicApi";
import { formatWithReference, toWholeLek, type MoneyReference } from "@/shared/utils/formatCurrency";
import type { ExchangeRateResponse } from "@/features/public/publicApi";

/** The studio a money amount belongs to — only these two fields decide whether lek is shown. */
export interface MoneyStudio {
  countryCode: string | null | undefined;
  currency:    string | null | undefined;
}

/** The lek frozen on a payment or purchase when the client paid (all null when none). With the rate,
 * any amount on that record (a refunded part, a remaining balance, a split) converts at the same rate. */
export interface FrozenReference {
  referenceAmount?:   number | null;
  referenceCurrency?: string | null;
  referenceRate?:     number | null;
}

/**
 * The latest Bank of Albania EUR/ALL rate. Only <Money> calls this, and only for an Albanian EUR
 * studio with no frozen lek, so every other studio makes no request at all. Cached by RTK Query
 * (no useEffect).
 */
export function useEurAllRate() {
  return useGetEurAllRateQuery();
}

/**
 * `amount` in lek at the rate frozen on its record (exact), or null when the record has none. With a
 * frozen rate any amount on the record converts at it (spec D5: refunds use the original rate); with
 * only the frozen total, that total is used as is (it is the record's whole amount).
 */
export function frozenReference(frozen: FrozenReference | null | undefined, amount: number): MoneyReference | null {
  if (!frozen?.referenceCurrency) return null;
  if (frozen.referenceRate != null) {
    return { amount: toWholeLek(amount, frozen.referenceRate), currency: frozen.referenceCurrency, approximate: false };
  }
  return frozen.referenceAmount != null
    ? { amount: frozen.referenceAmount, currency: frozen.referenceCurrency, approximate: false }
    : null;
}

/** For text (toasts, button labels, messages): `amount` with its frozen lek, or exactly formatCurrency. */
export function formatAtFrozenRate(amount: number, currency: string, frozen: FrozenReference | null | undefined): string {
  return formatWithReference(amount, currency, frozenReference(frozen, amount));
}

/** `amount` in lek at the latest rate, approximate; carries the rate's date when it is stale. */
export function referenceAtLatestRate(amount: number, rate: ExchangeRateResponse): MoneyReference {
  return {
    amount:        toWholeLek(amount, rate.rate),
    currency:      "ALL",
    approximate:   true,
    staleRateDate: rate.isStale ? rate.rateDate : null,
  };
}
