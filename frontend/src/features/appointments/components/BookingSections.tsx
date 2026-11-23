import { useState, type ReactNode } from "react";
import { bookingSectionHeadingId as headingId } from "./useBookingSections";
import { Button } from "@/shared/components/ui/button";
import { Money } from "@/shared/components/Money";
import type { MoneyStudio } from "@/shared/hooks/useEurAllRate";
import type { DepositEstimate } from "../depositEstimate";

/*
 * C02: the booking forms as ONE sectioned page with a final review, not a wizard.
 *
 * The requirement asks to compare staged steps with a well-sectioned single page before choosing a wizard, and
 * that comparison is a usability test nobody has run, so this builds the single page (the spec, slice 7 §5).
 * Back/Change is then a scroll and a focus call, not routing state to lose: every field stays mounted, so
 * nothing entered can be dropped by going back.
 */

interface BookingSectionProps {
  id:           string;
  number:       number;
  title:        string;
  description?: string;
  /** True when this section was opened from the review's Change button. */
  showBack:     boolean;
  onBack:       () => void;
  children:     ReactNode;
}

export function BookingSection({ id, number, title, description, showBack, onBack, children }: BookingSectionProps) {
  return (
    <section id={id} aria-labelledby={headingId(id)} className="space-y-4 border-t border-border/40 pt-5">
      <div className="space-y-0.5">
        <h2
          id={headingId(id)}
          tabIndex={-1}
          className="scroll-mt-24 rounded-sm text-sm font-semibold tracking-tight
                     focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <span className="text-muted-foreground">{number}.</span> {title}
        </h2>
        {description && <p className="text-xs text-muted-foreground">{description}</p>}
      </div>
      {children}
      {showBack && (
        <Button type="button" variant="outline" size="sm" onClick={onBack}>
          Back to review
        </Button>
      )}
    </section>
  );
}

export interface ReviewRow {
  label: string;
  value: ReactNode;
}

export interface ReviewGroup {
  /** The section a Change button returns to. */
  section: string;
  title:   string;
  rows:    ReviewRow[];
}

interface BookingReviewProps {
  id:       string;
  number:   number;
  groups:   ReviewGroup[];
  onChange: (sectionId: string) => void;
  /** The two statements C02's acceptance asks the review to make: what the deposit is, and what the request is. */
  deposit:  ReactNode;
  request:  ReactNode;
  children: ReactNode;
}

export function BookingReview({ id, number, groups, onChange, deposit, request, children }: BookingReviewProps) {
  return (
    <section id={id} aria-labelledby={headingId(id)} className="space-y-4 border-t border-border/40 pt-5">
      <div className="space-y-0.5">
        <h2
          id={headingId(id)}
          tabIndex={-1}
          className="scroll-mt-24 rounded-sm text-sm font-semibold tracking-tight
                     focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <span className="text-muted-foreground">{number}.</span> Review your request
        </h2>
        <p className="text-xs text-muted-foreground">
          Check the details before you send them. Changing one keeps everything else you&apos;ve entered.
        </p>
      </div>

      {groups.map((group) => (
        <div key={group.section} className="rounded-md border border-border/40">
          <div className="flex items-center justify-between gap-3 border-b border-border/40 px-3 py-1.5">
            <h3 className="text-xs font-semibold">{group.title}</h3>
            {/* aria-label, not a visually hidden span: the span's leading space was dropped from the accessible name
                ("Changeidea & references"). The label starts with the visible word, so speech input still works. */}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-9 px-3 text-xs"
              aria-label={`Change ${group.title.toLowerCase()}`}
              onClick={() => onChange(group.section)}
            >
              Change
            </Button>
          </div>
          <dl className="divide-y divide-border/30">
            {group.rows.map((row) => (
              <div key={row.label} className="grid grid-cols-[7rem_1fr] gap-3 px-3 py-2">
                <dt className="text-xs text-muted-foreground">{row.label}</dt>
                <dd className="min-w-0 break-words text-sm">{row.value}</dd>
              </div>
            ))}
          </dl>
        </div>
      ))}

      <dl className="divide-y divide-border/30 rounded-md border border-border/40 bg-muted/20">
        <div className="grid grid-cols-[7rem_1fr] gap-3 px-3 py-2">
          <dt className="text-xs text-muted-foreground">Deposit</dt>
          <dd className="min-w-0 break-words text-sm" data-testid="review-deposit">{deposit}</dd>
        </div>
        <div className="grid grid-cols-[7rem_1fr] gap-3 px-3 py-2">
          <dt className="text-xs text-muted-foreground">Your request</dt>
          <dd className="min-w-0 break-words text-sm" data-testid="review-request">{request}</dd>
        </div>
      </dl>

      {children}
    </section>
  );
}

/**
 * The deposit as the review states it. Every figure is called an estimate, because the server sets the amount
 * when it saves the request (codes and credit included) and that is the amount anyone is asked to pay.
 */
export function DepositReviewValue({ estimate, currency, studio, discountPending, staff }: {
  estimate:        DepositEstimate;
  currency:        string | undefined;
  studio:          MoneyStudio;
  /** A promo code, referral code or referral credit is entered: the server applies it after this figure. */
  discountPending: boolean;
  /** Staff booking for a client: "the client pays", not "you pay". */
  staff?:          boolean;
}) {
  const noCharge = staff ? "Sending this request charges nothing." : "Sending this request doesn't charge you anything.";
  switch (estimate.kind) {
    case "package":
      return <>None. A session from {staff ? "the client's" : "your"} package covers this booking.</>;
    case "none":
      return <>None expected for this booking.</>;
    case "awaiting-artist":
      return (
        <>
          Worked out from the artist&apos;s rate once {estimate.studioAssigns ? "the studio assigns one" : "you choose one"}.{" "}
          <span className="text-xs text-muted-foreground">{noCharge}</span>
        </>
      );
    case "amount":
      return (
        <>
          <span className="font-medium tabular-nums">
            {currency ? <Money amount={estimate.amount} currency={currency} studio={studio} /> : "…"}
          </span>{" "}
          estimated{estimate.basis === "service" ? ", set by the service" : ""}.{" "}
          <span className="text-xs text-muted-foreground">
            The exact amount is worked out when your request is saved{discountPending ? ", after any code or credit" : ""}.{" "}
            {noCharge}
          </span>
        </>
      );
  }
}

/**
 * Promotion and referral codes behind a disclosure (C02: "collapse optional promotion/referral fields"). Starts
 * open only when a code is already filled in; a code can only be typed with it open, and it stays open after, so
 * the review's "Codes" row is the reminder rather than a forced-open panel the person can't close.
 */
export function OptionalCodes({ defaultOpen, children }: { defaultOpen: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <details
      open={open}
      onToggle={(e) => setOpen(e.currentTarget.open)}
      className="rounded-md border border-border/40 px-3 py-2"
    >
      <summary className="cursor-pointer select-none py-1 text-xs font-medium">
        Promo or referral code (optional)
      </summary>
      <div className="space-y-4 pt-3">{children}</div>
    </details>
  );
}
