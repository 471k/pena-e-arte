import { useGetEurAllRateQuery } from "@/features/public/publicApi";
import { toWholeLek, type MoneyReference } from "@/shared/utils/formatCurrency";
import type { ExchangeRateResponse } from "@/features/public/publicApi";

/** The studio a money amount belongs to — only these two fields decide whether lek is shown. */
export interface MoneyStudio {
  countryCode: string | null | undefined;
  currency:    string | null | undefined;
}

/** The lek frozen on a payment or purchase when the client paid (all null when none). */
export interface FrozenReference {
  referenceAmount?:   number | null;
  referenceCurrency?: string | null;
}

/**
 * The latest Bank of Albania EUR/ALL rate. Only <Money> calls this, and only for an Albanian EUR
 * studio with no frozen lek, so every other studio makes no request at all. Cached by RTK Query
 * (no useEffect).
 */
export function useEurAllRate() {
  return useGetEurAllRateQuery();
}

/** The frozen lek as a MoneyReference, or null when the record has none. */
export function frozenReference(frozen: FrozenReference | null | undefined): MoneyReference | null {
  return frozen?.referenceAmount != null && frozen.referenceCurrency
    ? { amount: frozen.referenceAmount, currency: frozen.referenceCurrency, approximate: false }
    : null;
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
