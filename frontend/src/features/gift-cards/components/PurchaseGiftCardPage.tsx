import { useState } from "react";
import { useParams } from "react-router-dom";
import { toast } from "sonner";
import { AlertCircle, Gift, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";
import { useGetPaymentCapabilitiesQuery } from "@/features/payments/paymentsApi";
import { usePurchaseGiftCardMutation } from "../giftCardsApi";

const PRESET_AMOUNTS = [25, 50, 100, 200];

/** Client/guest gift-card purchase. Gated on the same GET /api/v1/payments/capabilities check
 * the deposit-checkout flow already uses — no second capability-detection mechanism. Card
 * collection itself (POK Elements-equivalent) doesn't exist yet in this codebase (NullPaymentProvider
 * fails closed), so this surfaces the same "temporarily unavailable" messaging DepositCheckoutPage
 * shows rather than a broken card form — it will light up automatically once POK lands. */
export function PurchaseGiftCardPage() {
  useDocumentMeta({ title: "Buy a Gift Card — TattooOS", canonical: "/gift-cards/buy" });

  const { slug } = useParams<{ slug: string }>();
  // GET /api/v1/payments/capabilities requires an authenticated caller (RequireAuthorization()
  // on the whole group) — skip it for an anonymous guest rather than let a 401 trip baseQuery's
  // refresh/session-expired handling for a visitor who was never logged in. A guest just attempts
  // the purchase directly; a real provider failure surfaces as a normal error toast.
  const isAuthenticated = !!useAppSelector((s) => s.auth.token);
  const { data: capabilities } = useGetPaymentCapabilitiesQuery(undefined, { skip: !isAuthenticated });
  const cardPaymentsAvailable = !isAuthenticated || capabilities?.cardPaymentsAvailable !== false;

  const [amount, setAmount] = useState<number>(50);
  const [purchaserEmail, setPurchaserEmail] = useState("");
  const [recipientEmail, setRecipientEmail] = useState("");
  const [purchase, { isLoading, isSuccess }] = usePurchaseGiftCardMutation();

  async function handleSubmit() {
    if (!slug || !purchaserEmail) return;
    const result = await purchase({
      studioSlug: slug,
      amount,
      purchaserEmail,
      recipientEmail: recipientEmail || null,
    });
    if ("data" in result) toast.success("Gift card purchase started — check your email to complete payment.");
    else toast.error("Failed to start gift card purchase.");
  }

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base"><Gift className="h-5 w-5" />Buy a gift card</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {isSuccess ? (
            <p className="text-sm text-center text-muted-foreground py-4">
              Thanks! Check your email to finish paying for the gift card.
            </p>
          ) : !cardPaymentsAvailable ? (
            <div className="flex flex-col items-center gap-3 py-8 text-center">
              <AlertCircle className="h-8 w-8 text-destructive" />
              <p className="text-sm text-destructive-text">
                Gift card purchases are temporarily unavailable. Please contact the studio directly.
              </p>
            </div>
          ) : (
            <>
              <div className="space-y-1.5">
                <Label>Amount (€)</Label>
                <div className="flex flex-wrap gap-2">
                  {PRESET_AMOUNTS.map((preset) => (
                    <Button
                      key={preset} type="button" size="sm"
                      variant={amount === preset ? "default" : "outline"}
                      onClick={() => setAmount(preset)}
                    >
                      €{preset}
                    </Button>
                  ))}
                </div>
                <Input
                  type="number" min="1" step="1" value={amount}
                  onChange={(e) => setAmount(Number(e.target.value))}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="gift-card-purchaser-email">Your email</Label>
                <Input id="gift-card-purchaser-email" type="email" value={purchaserEmail} onChange={(e) => setPurchaserEmail(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="gift-card-recipient-email">Recipient email (optional)</Label>
                <Input id="gift-card-recipient-email" type="email" value={recipientEmail} onChange={(e) => setRecipientEmail(e.target.value)} />
              </div>
              <Button className="w-full gap-1.5" disabled={isLoading || !purchaserEmail || amount <= 0} onClick={handleSubmit}>
                {isLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                Buy gift card
              </Button>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
