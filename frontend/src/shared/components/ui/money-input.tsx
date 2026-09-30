"use client";
import * as React from "react";
import { Input, type InputProps } from "./input";
import { currencyLabel, currencyMinorUnits } from "@/shared/utils/formatCurrency";
import { cn } from "@/shared/utils/cn";

export interface MoneyInputProps extends Omit<InputProps, "type" | "step"> {
  currency: string;
}

/**
 * Drop-in replacement for `<Input type="number" step="0.01" ...>` in every price/deposit/amount
 * field — a leading currency adornment (symbol or ISO code, from currencyLabel) and a `step`
 * derived from the currency's own minor units (1 for JPY, 0.01 for EUR/ALL, 0.001 for KWD),
 * instead of a hardcoded two-decimal step that silently rounded/truncated a 3-decimal currency.
 * Forwards a ref and spreads the rest of its props straight onto the underlying <Input>, so it
 * still works with React Hook Form's `register(field)` spread — no Controller wrapping needed.
 * Decimal-place validation itself stays with each form's own resolver (see
 * hasAtMostCurrencyMinorUnits/tooManyDecimalsMessage below) so the existing
 * `errors.<field>.message` paragraph pattern keeps working unchanged.
 */
export const MoneyInput = React.forwardRef<HTMLInputElement, MoneyInputProps>(
  ({ currency, className, ...props }, ref) => {
    const step = (10 ** -currencyMinorUnits(currency)).toString();

    return (
      <div className="relative">
        <span
          aria-hidden="true"
          className="pointer-events-none absolute inset-y-0 left-3 flex items-center text-sm text-muted-foreground"
        >
          {currencyLabel(currency)}
        </span>
        <Input ref={ref} type="number" step={step} min="0" className={cn("pl-12", className)} {...props} />
      </div>
    );
  },
);
MoneyInput.displayName = "MoneyInput";

/** Client-side mirror of CurrencyCatalog.HasAtMostMinorUnits — same rounding rule, so a form can
 * reject an over-precise amount before it ever reaches the server's 400. */
export function hasAtMostCurrencyMinorUnits(amount: number, currency: string): boolean {
  const digits = currencyMinorUnits(currency);
  const factor = 10 ** digits;
  return Math.round(amount * factor) / factor === amount;
}

/** Matches the backend's BusinessRuleViolationException wording exactly (CardCurrencyGuard /
 * CreatePaymentIntentCommand / PurchaseGiftCardCommand), so the same sentence appears whether the
 * client or the server catches it first. */
export function tooManyDecimalsMessage(currency: string): string {
  return `Amount has more decimal places than ${currency} allows.`;
}
