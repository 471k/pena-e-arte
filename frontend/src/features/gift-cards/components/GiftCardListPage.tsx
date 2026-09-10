import { toast } from "sonner";
import { Gift } from "lucide-react";
import { Badge } from "@/shared/components/ui/badge";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetGiftCardsQuery, useVoidGiftCardMutation } from "../giftCardsApi";
import { GiftCardStatus, type GiftCardResponse } from "../giftCards.types";

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

const STATUS_STYLES: Record<GiftCardStatus, string> = {
  [GiftCardStatus.Pending]:  "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  [GiftCardStatus.Active]:   "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  [GiftCardStatus.Redeemed]: "border-slate-300 bg-slate-100 text-slate-800 hover:bg-slate-100",
  [GiftCardStatus.Voided]:   "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
};

function StatusBadge({ status }: { status: GiftCardStatus }) {
  return <Badge variant="outline" className={cn(STATUS_STYLES[status])}>{status}</Badge>;
}

function VoidButton({ card }: { card: GiftCardResponse }) {
  const [voidCard, { isLoading }] = useVoidGiftCardMutation();

  async function handleClick() {
    const result = await voidCard(card.id);
    if ("data" in result) toast.success("Gift card voided.");
    else toast.error("Failed to void gift card.");
  }

  if (card.status === GiftCardStatus.Voided || card.status === GiftCardStatus.Redeemed) return null;
  return (
    <Button
      variant="ghost" size="sm" className="h-7 text-xs text-destructive-text hover:text-destructive-text"
      disabled={isLoading} onClick={(e) => { e.stopPropagation(); handleClick(); }}
    >
      Void
    </Button>
  );
}

/** Owner-only gift-card list, balance + status per card, void action. */
export function GiftCardListPage() {
  useDocumentMeta({ title: "Gift Cards — TattooOS", canonical: "/gift-cards" });

  const { data: cards, isLoading, isError } = useGetGiftCardsQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Gift className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Gift Cards</span>
        </div>
        {cards && (
          <span className="text-xs text-muted-foreground">
            {cards.length} card{cards.length !== 1 ? "s" : ""}
          </span>
        )}
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading gift cards">
            {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-14 w-full rounded-lg" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load gift cards. Please try again.
          </p>
        )}

        {!isLoading && !isError && cards?.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-20 text-center">
            <Gift className="h-10 w-10 text-muted-foreground/50" />
            <p className="text-sm font-medium text-foreground">No gift cards issued yet</p>
            <p className="text-xs text-muted-foreground">
              Cards purchased by clients appear here once payment is confirmed.
            </p>
          </div>
        )}

        {!isLoading && !isError && cards && cards.length > 0 && (
          <DataTable<GiftCardResponse>
            columns={[
              { header: "Code", cell: (c) => <span className="font-mono text-sm">{c.code}</span> },
              { header: "Balance", cell: (c) => <span className="font-semibold">{formatCurrency(c.remainingBalance)} / {formatCurrency(c.initialBalance)}</span> },
              { header: "Status", cell: (c) => <StatusBadge status={c.status} /> },
              { header: "Purchased", cell: (c) => formatDate(c.createdAt) },
              { header: "", cell: (c) => <VoidButton card={c} /> },
            ]}
            data={cards}
            keyExtractor={(c) => c.id}
            mobileCard={(c) => (
              <div className="space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-mono text-sm">{c.code}</span>
                  <StatusBadge status={c.status} />
                </div>
                <div className="flex items-center justify-between gap-2 text-sm">
                  <span>{formatCurrency(c.remainingBalance)} / {formatCurrency(c.initialBalance)}</span>
                  <VoidButton card={c} />
                </div>
              </div>
            )}
          />
        )}
      </main>
    </div>
  );
}
