import { useState } from "react";
import { Banknote, Check, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useConfirmCashDepositMutation } from "@/features/payments/paymentsApi";

interface CashDepositConfirmButtonProps {
  paymentId:  string;
  clientName: string;
  amount:     number;
}

export function CashDepositConfirmButton({
  paymentId,
  clientName,
  amount,
}: CashDepositConfirmButtonProps) {
  const [confirm, setConfirm]        = useState(false);
  const [confirmCash, { isLoading }] = useConfirmCashDepositMutation();

  async function handleConfirm() {
    await confirmCash(paymentId);
    setConfirm(false);
  }

  if (confirm) {
    return (
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">
          Confirm €{amount.toFixed(2)} cash received from {clientName}?
        </span>
        <Button
          size="sm"
          className="h-7 px-2 text-xs gap-1"
          disabled={isLoading}
          onClick={handleConfirm}
        >
          {isLoading
            ? <Loader2 className="h-3 w-3 animate-spin" />
            : <><Check className="h-3 w-3" /> Yes</>}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          className="h-7 px-2 text-xs"
          onClick={() => setConfirm(false)}
        >
          Cancel
        </Button>
      </div>
    );
  }

  return (
    <Button
      size="sm"
      variant="outline"
      className="h-7 px-2 text-xs gap-1"
      onClick={() => setConfirm(true)}
    >
      <Banknote className="h-3.5 w-3.5" />
      Mark cash received
    </Button>
  );
}
