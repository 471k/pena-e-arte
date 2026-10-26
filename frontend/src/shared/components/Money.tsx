import { formatCurrency, formatWithReference, lekReferenceApplies } from "@/shared/utils/formatCurrency";
import {
  frozenReference,
  referenceAtLatestRate,
  useEurAllRate,
  type FrozenReference,
  type MoneyStudio,
} from "@/shared/hooks/useEurAllRate";

interface MoneyProps {
  amount:     number;
  currency:   string;
  /** The studio the amount belongs to; lek is shown only for Albanian EUR studios. */
  studio?:    MoneyStudio | null;
  /** The lek frozen on the payment or purchase, once the client has paid. */
  frozen?:    FrozenReference | null;
  className?: string;
}

/**
 * A client-facing money amount (docs/specs/spec-eur-with-lek-display-2026-10-26.md): "€50", or for
 * Albanian EUR studios "€50 (≈ ALL 4,537)" before payment and "€50 (ALL 4,537)" once paid. EUR first,
 * the lek in the same element, same size and weight, so a screen reader reads both; it may wrap at
 * narrow widths but is never truncated. Picks, in order: the frozen lek; else, for an AL + EUR studio,
 * the latest BoA rate (the only case that fetches it); else the plain amount, exactly as before.
 */
export function Money({ amount, currency, studio, frozen, className }: MoneyProps) {
  const frozenLek = frozenReference(frozen);
  if (frozenLek) {
    return <span className={className}>{formatWithReference(amount, currency, frozenLek)}</span>;
  }
  if (!lekReferenceApplies(studio?.countryCode, currency)) {
    return <span className={className}>{formatCurrency(amount, currency)}</span>;
  }
  return <MoneyAtLatestRate amount={amount} currency={currency} className={className} />;
}

function MoneyAtLatestRate({ amount, currency, className }: { amount: number; currency: string; className?: string }) {
  // While the rate loads, or if it fails (404 before the first fetch, network), the euro amount
  // shows alone — never a bare number and no error for the client.
  const { data: rate } = useEurAllRate();
  const reference = rate ? referenceAtLatestRate(amount, rate) : null;
  return <span className={className}>{formatWithReference(amount, currency, reference)}</span>;
}
