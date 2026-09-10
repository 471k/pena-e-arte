import { toast } from "sonner";
import { AlertCircle, Package as PackageIcon, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPaymentCapabilitiesQuery } from "@/features/payments/paymentsApi";
import { useGetPackagesQuery, usePurchasePackageMutation } from "../packagesApi";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

/** Client package purchase — parallels PurchaseGiftCardPage; same capabilities gate, no second
 * capability-detection mechanism. */
export function PurchasePackagePage() {
  useDocumentMeta({ title: "Buy a Package — TattooOS", canonical: "/packages/buy" });

  const { data: packages, isLoading } = useGetPackagesQuery();
  const { data: capabilities } = useGetPaymentCapabilitiesQuery();
  const cardPaymentsAvailable = capabilities?.cardPaymentsAvailable !== false;
  const [purchase, { isLoading: isPurchasing }] = usePurchasePackageMutation();

  const activePackages = packages?.filter((p) => p.isActive) ?? [];

  async function handlePurchase(packageId: string) {
    const result = await purchase({ packageId });
    if ("data" in result) toast.success("Package purchase started — check your email to complete payment.");
    else toast.error("Failed to start package purchase.");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <PackageIcon className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Buy a Package</span>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        {!cardPaymentsAvailable && (
          <div className="flex flex-col items-center gap-3 py-8 text-center">
            <AlertCircle className="h-8 w-8 text-destructive" />
            <p className="text-sm text-destructive-text">
              Package purchases are temporarily unavailable. Please contact the studio directly.
            </p>
          </div>
        )}

        {isLoading && (
          <div className="space-y-3" aria-label="Loading packages">
            {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-lg" />)}
          </div>
        )}

        {!isLoading && cardPaymentsAvailable && activePackages.length === 0 && (
          <p className="text-sm text-muted-foreground text-center py-16">
            No packages are available for purchase right now.
          </p>
        )}

        {!isLoading && cardPaymentsAvailable && activePackages.map((pkg) => (
          <Card key={pkg.id}>
            <CardHeader>
              <CardTitle className="text-base">{pkg.name}</CardTitle>
            </CardHeader>
            <CardContent className="flex items-center justify-between">
              <p className="text-sm text-muted-foreground">{pkg.sessionCount} sessions</p>
              <div className="flex items-center gap-3">
                <span className="font-semibold">{formatCurrency(pkg.price)}</span>
                <Button size="sm" disabled={isPurchasing} onClick={() => handlePurchase(pkg.id)} className="gap-1.5">
                  {isPurchasing && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                  Buy
                </Button>
              </div>
            </CardContent>
          </Card>
        ))}
      </main>
    </div>
  );
}
