import { useNavigate } from "react-router-dom";
import { Plus, Tag } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPromoCodesQuery } from "../promoCodesApi";
import { PromoCodeCard } from "./PromoCodeCard";

export function PromoCodeListPage() {
  useDocumentMeta({ title: "Promo Codes — TattooOS", canonical: "/promo-codes" });

  const navigate = useNavigate();
  const { data: promoCodes, isLoading, isError } = useGetPromoCodesQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Tag className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Promo Codes</span>
        </div>
        <div className="flex items-center gap-3">
          {promoCodes && (
            <span className="text-xs text-muted-foreground">
              {promoCodes.length} code{promoCodes.length !== 1 ? "s" : ""}
            </span>
          )}
          <Button size="sm" onClick={() => navigate("/promo-codes/new")} className="gap-1.5">
            <Plus className="h-3.5 w-3.5" />
            New Code
          </Button>
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading promo codes">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-14 w-full rounded-lg" />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load promo codes. Please try again.
          </p>
        )}

        {!isLoading && !isError && promoCodes?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <Tag className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No promo codes yet</p>
              <p className="text-xs text-muted-foreground">
                Create a code to offer clients a discount on their deposit at booking.
              </p>
            </div>
            <Button size="sm" onClick={() => navigate("/promo-codes/new")}>
              Create code
            </Button>
          </div>
        )}

        {!isLoading && !isError && promoCodes && promoCodes.length > 0 && promoCodes.map((promoCode) => (
          <PromoCodeCard key={promoCode.id} promoCode={promoCode} />
        ))}
      </main>
    </div>
  );
}
