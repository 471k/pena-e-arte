import { useState } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { ArrowLeft, CreditCard, ExternalLink, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { useConnectStudioMutation } from "../studiosApi";

// Full list of countries supported by Stripe Connect (Express accounts)
const COUNTRIES = [
  { code: "AU", label: "Australia" },
  { code: "AT", label: "Austria" },
  { code: "BE", label: "Belgium" },
  { code: "BR", label: "Brazil" },
  { code: "BG", label: "Bulgaria" },
  { code: "CA", label: "Canada" },
  { code: "HR", label: "Croatia" },
  { code: "CY", label: "Cyprus" },
  { code: "CZ", label: "Czech Republic" },
  { code: "DK", label: "Denmark" },
  { code: "EE", label: "Estonia" },
  { code: "FI", label: "Finland" },
  { code: "FR", label: "France" },
  { code: "DE", label: "Germany" },
  { code: "GH", label: "Ghana" },
  { code: "GI", label: "Gibraltar" },
  { code: "GR", label: "Greece" },
  { code: "HK", label: "Hong Kong" },
  { code: "HU", label: "Hungary" },
  { code: "IN", label: "India" },
  { code: "ID", label: "Indonesia" },
  { code: "IE", label: "Ireland" },
  { code: "IL", label: "Israel" },
  { code: "IT", label: "Italy" },
  { code: "JP", label: "Japan" },
  { code: "KE", label: "Kenya" },
  { code: "LV", label: "Latvia" },
  { code: "LI", label: "Liechtenstein" },
  { code: "LT", label: "Lithuania" },
  { code: "LU", label: "Luxembourg" },
  { code: "MY", label: "Malaysia" },
  { code: "MT", label: "Malta" },
  { code: "MX", label: "Mexico" },
  { code: "NL", label: "Netherlands" },
  { code: "NZ", label: "New Zealand" },
  { code: "NG", label: "Nigeria" },
  { code: "NO", label: "Norway" },
  { code: "PL", label: "Poland" },
  { code: "PT", label: "Portugal" },
  { code: "RO", label: "Romania" },
  { code: "SG", label: "Singapore" },
  { code: "SK", label: "Slovakia" },
  { code: "SI", label: "Slovenia" },
  { code: "ZA", label: "South Africa" },
  { code: "ES", label: "Spain" },
  { code: "SE", label: "Sweden" },
  { code: "CH", label: "Switzerland" },
  { code: "TH", label: "Thailand" },
  { code: "TT", label: "Trinidad & Tobago" },
  { code: "AE", label: "United Arab Emirates" },
  { code: "GB", label: "United Kingdom" },
  { code: "US", label: "United States" },
  { code: "UY", label: "Uruguay" },
];

const SELECT_CLS = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50",
);

export function ConnectStudioPage() {
  const navigate  = useNavigate();
  const location  = useLocation();
  const isUpdate  = (location.state as { isUpdate?: boolean } | null)?.isUpdate === true;
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
      const err = result.error as { data?: { message?: string } } | undefined;
      setError(err?.data?.message ?? "Failed to start Stripe onboarding. Please try again.");
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
          <span className="font-semibold tracking-tight">
            {isUpdate ? "Update Stripe account" : "Connect with Stripe"}
          </span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        {isUpdate ? (
          <p className="text-sm text-muted-foreground">
            Update your connected Stripe account details. You'll be redirected to Stripe to complete
            the process and then returned here.
          </p>
        ) : (
          <>
            <p className="text-sm text-muted-foreground">
              Connect your studio to Stripe to accept deposit payments from clients. You'll be
              redirected to Stripe to complete the onboarding process.
            </p>
            <ul className="space-y-1.5 text-sm text-muted-foreground list-disc list-inside">
              <li>Accepts all major credit and debit cards</li>
              <li>Deposits held and released automatically</li>
              <li>Payouts directly to your bank account</li>
            </ul>
          </>
        )}

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
          <p className="text-xs text-muted-foreground">
            Only countries supported by Stripe Connect are listed. If yours isn't here, Stripe payouts may not be available in your region yet.
          </p>
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
