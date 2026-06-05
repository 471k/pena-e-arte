import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, CreditCard, ExternalLink, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useConnectStudioMutation } from "../studiosApi";

const COUNTRIES = [
  { code: "US", label: "United States" },
  { code: "GB", label: "United Kingdom" },
  { code: "DE", label: "Germany" },
  { code: "FR", label: "France" },
  { code: "ES", label: "Spain" },
  { code: "IT", label: "Italy" },
  { code: "NL", label: "Netherlands" },
  { code: "AU", label: "Australia" },
  { code: "CA", label: "Canada" },
  { code: "PT", label: "Portugal" },
  { code: "BR", label: "Brazil" },
  { code: "OTHER", label: "Other" },
];

const SELECT_CLS = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50",
);

export function ConnectStudioPage() {
  const navigate = useNavigate();
  const [connectStudio, { isLoading }] = useConnectStudioMutation();
  const [country,    setCountry]    = useState("");
  const [error,      setError]      = useState<string | null>(null);

  async function onConnect() {
    if (!country) return;
    setError(null);

    const origin     = window.location.origin;
    const returnUrl  = `${origin}/studio/connect/return`;
    const refreshUrl = `${origin}/studio/connect/refresh`;

    const result = await connectStudio({ country, returnUrl, refreshUrl });
    if ("error" in result) {
      setError("Failed to start Stripe onboarding. Please try again.");
      return;
    }
    window.location.href = result.data.onboardingUrl;
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/billing")}
          className="gap-1.5"
          disabled={isLoading}
        >
          <ArrowLeft className="h-4 w-4" />
          Billing
        </Button>
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Connect with Stripe</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <p className="text-sm text-muted-foreground">
          Connect your studio to Stripe to accept deposit payments from clients. You'll be redirected
          to Stripe to complete the onboarding process.
        </p>

        <ul className="space-y-1.5 text-sm text-muted-foreground list-disc list-inside">
          <li>Accepts all major credit and debit cards</li>
          <li>Deposits held and released automatically</li>
          <li>Payouts directly to your bank account</li>
        </ul>

        <div className="space-y-1.5">
          <Label htmlFor="country">Country</Label>
          <select
            id="country"
            value={country}
            onChange={(e) => setCountry(e.target.value)}
            disabled={isLoading}
            className={cn(SELECT_CLS, !country && "text-muted-foreground")}
          >
            <option value="">Select your country…</option>
            {COUNTRIES.map((c) => (
              <option key={c.code} value={c.code}>{c.label}</option>
            ))}
          </select>
        </div>

        {error && <p className="text-sm text-destructive">{error}</p>}

        <Button
          className="w-full gap-2"
          disabled={!country || isLoading}
          onClick={onConnect}
        >
          {isLoading ? (
            <>
              <Loader2 className="h-4 w-4 animate-spin" />
              Redirecting to Stripe…
            </>
          ) : (
            <>
              <ExternalLink className="h-4 w-4" />
              Continue to Stripe
            </>
          )}
        </Button>

        <p className="text-xs text-muted-foreground text-center">
          You'll be redirected to Stripe's secure onboarding. Return here when done.
        </p>
      </main>
    </div>
  );
}
