import { useState } from "react";
import { toast } from "sonner";
import { Gift, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { useRedeemGiftCardMutation } from "../giftCardsApi";

interface RedeemGiftCardFieldProps {
  appointmentId: string;
  amount: number;
}

/** "Redeem gift card" field for the deposit/payment step — visually distinct from a promo-code
 * field (none exists on this branch; Group 4's PromoCode isn't merged here). Applying a code
 * reduces the appointment's real deposit server-side immediately; the card tab above reads a
 * snapshot of that amount when its own payment intent is created, so redeeming after the card
 * tab has already loaded needs a page refresh to pick up the new (lower) amount — a known
 * follow-up, not silently claimed as fully live-synced. */
export function RedeemGiftCardField({ appointmentId, amount }: RedeemGiftCardFieldProps) {
  const [code, setCode] = useState("");
  const [redeem, { isLoading, isSuccess }] = useRedeemGiftCardMutation();

  async function handleApply() {
    if (!code.trim()) return;
    const result = await redeem({ code: code.trim().toUpperCase(), appointmentId, amount });
    if ("data" in result) {
      toast.success("Gift card applied — refresh this page to see the updated amount due.");
      setCode("");
    } else {
      toast.error("That gift card code couldn't be applied.");
    }
  }

  if (isSuccess) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-green-700">
        <Gift className="h-3.5 w-3.5" />
        Gift card applied.
      </p>
    );
  }

  return (
    <div className="flex items-end gap-2">
      <div className="flex-1 space-y-1">
        <label htmlFor="redeem-gift-card-code" className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
          <Gift className="h-3.5 w-3.5" />
          Gift card code
        </label>
        <Input
          id="redeem-gift-card-code"
          placeholder="e.g. AB12CD34EF56"
          value={code}
          onChange={(e) => setCode(e.target.value)}
          className="font-mono uppercase"
        />
      </div>
      <Button type="button" variant="outline" size="sm" disabled={isLoading || !code.trim()} onClick={handleApply}>
        {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Apply"}
      </Button>
    </div>
  );
}
