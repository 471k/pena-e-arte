import { useState } from "react";
import { CheckCircle2, CreditCard, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  useConnectPokAccountMutation,
  useGetPokConnectionStatusQuery,
} from "@/features/payments/paymentsApi";

function extractErrorMessage(err: unknown, fallback: string): string {
  return err && typeof err === "object" && "data" in err && err.data &&
    typeof err.data === "object" && "message" in err.data
    ? String((err.data as { message: string }).message)
    : fallback;
}

/**
 * ADR-0001: every studio brings its own POK merchant account — there is no platform-level key.
 * keyId/keySecret go straight to this form's POST and are never shown again; only the connected
 * merchantId is displayed afterward. See docs/payments/ADR-0001-payment-providers.md.
 */
export function PokSettingsCard() {
  const { data: status, isLoading: statusLoading } = useGetPokConnectionStatusQuery();
  const [connect, { isLoading: isConnecting }] = useConnectPokAccountMutation();

  const [keyId, setKeyId] = useState("");
  const [keySecret, setKeySecret] = useState("");
  const [merchantId, setMerchantId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);

  async function handleConnect() {
    setError(null);
    try {
      await connect({ keyId: keyId.trim(), keySecret: keySecret.trim(), merchantId: merchantId.trim() }).unwrap();
      setKeyId("");
      setKeySecret("");
      setMerchantId("");
      setEditing(false);
      toast.success("POK account connected.");
    } catch (err: unknown) {
      setError(extractErrorMessage(err, "Could not connect this POK account. Check your credentials and try again."));
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base flex items-center gap-2">
          <CreditCard className="h-4 w-4" /> Card deposits — POK
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-muted-foreground">
          Connect your studio's own POK merchant account to accept card deposits. Get your{" "}
          <code className="text-xs">keyId</code> / <code className="text-xs">keySecret</code> and
          merchant ID from your POK dashboard's API Keys section — we never generate these for you.
        </p>
        <p className="text-xs text-muted-foreground">
          POK only opens merchant accounts for businesses registered in Albania. If your studio
          isn't registered there, POK won't be able to approve an account for you — use Cash for
          deposits instead.
        </p>

        {statusLoading && (
          <div className="flex items-center gap-2 text-sm text-muted-foreground py-2">
            <Loader2 className="h-4 w-4 animate-spin" /> Checking connection…
          </div>
        )}

        {!statusLoading && status?.connected && !editing && (
          <div className="rounded-md border px-3 py-3 space-y-2">
            <p className="flex items-center gap-2 text-sm font-medium text-green-600 dark:text-green-400">
              <CheckCircle2 className="h-4 w-4" /> Connected
            </p>
            <p className="text-xs text-muted-foreground font-mono">Merchant ID: {status.merchantId}</p>
            <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
              Reconnect / change account
            </Button>
          </div>
        )}

        {!statusLoading && (!status?.connected || editing) && (
          <div className="space-y-2">
            <div className="space-y-1">
              <Label htmlFor="pok-key-id">Key ID</Label>
              <Input id="pok-key-id" value={keyId} onChange={(e) => setKeyId(e.target.value)} autoComplete="off" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="pok-key-secret">Key Secret</Label>
              <Input
                id="pok-key-secret" type="password" value={keySecret}
                onChange={(e) => setKeySecret(e.target.value)} autoComplete="off"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="pok-merchant-id">Merchant ID</Label>
              <Input id="pok-merchant-id" value={merchantId} onChange={(e) => setMerchantId(e.target.value)} autoComplete="off" />
            </div>
            {error && <p className="text-xs text-destructive-text">{error}</p>}
            <div className="flex gap-2">
              <Button
                size="sm"
                onClick={() => void handleConnect()}
                disabled={isConnecting || !keyId.trim() || !keySecret.trim() || !merchantId.trim()}
              >
                {isConnecting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Connect"}
              </Button>
              {status?.connected && (
                <Button variant="outline" size="sm" onClick={() => setEditing(false)} disabled={isConnecting}>
                  Cancel
                </Button>
              )}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
