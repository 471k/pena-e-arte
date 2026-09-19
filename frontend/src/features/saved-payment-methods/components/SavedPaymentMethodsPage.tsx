import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { AddCardForm } from "@nebula-ltd/pok-payments-js/react";
import type { AddCardData, PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";
import { CreditCard, Loader2, Star, Trash2 } from "lucide-react";
import { useAppSelector } from "@/app/hooks";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetPaymentCapabilitiesQuery } from "@/features/payments/paymentsApi";
import {
  useGetSavedPaymentMethodsQuery,
  useAddSavedPaymentMethodMutation,
  useDeleteSavedPaymentMethodMutation,
} from "../savedPaymentMethodsApi";
import type { SavedPaymentMethodResponse } from "../savedPaymentMethod.types";

function CardRow({ method }: { method: SavedPaymentMethodResponse }) {
  const [deleteMethod, { isLoading }] = useDeleteSavedPaymentMethodMutation();

  async function handleDelete() {
    const result = await deleteMethod(method.id);
    if ("error" in result) {
      toast.error("Failed to remove card. Please try again.");
    } else {
      toast.success("Card removed.");
    }
  }

  return (
    <Card>
      <CardContent className="p-4 flex items-center gap-4">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-blue-500/10 text-blue-600">
          <CreditCard className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1 space-y-1">
          <p className="text-sm font-medium leading-none">
            {method.cardBrand ?? "Card on file"} {method.maskedPan ?? ""}
          </p>
          <p className="text-xs text-muted-foreground">
            {method.expiryMonth && method.expiryYear
              ? `Expires ${method.expiryMonth}/${method.expiryYear}`
              : "Saved card"}
          </p>
        </div>
        {method.isDefault && (
          <span className="flex items-center gap-1 text-xs px-2 py-0.5 rounded-full font-medium bg-amber-500/10 text-amber-700">
            <Star className="h-3 w-3" />
            Default
          </span>
        )}
        <Button
          variant="outline"
          size="sm"
          onClick={() => void handleDelete()}
          disabled={isLoading}
          className="gap-1.5 text-destructive-text hover:text-destructive-text"
        >
          {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
          Remove
        </Button>
      </CardContent>
    </Card>
  );
}

export function SavedPaymentMethodsPage() {
  useDocumentMeta({ title: "Payment Methods — TattooOS", canonical: "/clients/me/payment-methods" });

  // A card is saved against a specific studio's POK merchant (ADR-0001) — a client who
  // hasn't joined any studio yet has nowhere to route a saved card to. Skip both queries
  // in that case and show the same "browse studios" prompt used on the Book/Profile pages,
  // rather than the payment-capabilities check silently reporting cards "unavailable".
  const hasStudio = useAppSelector((s) => s.auth.tenantId) != null;

  const { data: methods, isLoading, isError, refetch } = useGetSavedPaymentMethodsQuery(undefined, { skip: !hasStudio });
  const { data: capabilities, isLoading: isLoadingCapabilities } = useGetPaymentCapabilitiesQuery(undefined, { skip: !hasStudio });
  const [addMethod, { isLoading: isAdding }] = useAddSavedPaymentMethodMutation();
  const [formKey, setFormKey] = useState(0);

  const pokEnvironment = capabilities?.pokEnvironment;
  const cardsAvailable = capabilities?.cardPaymentsAvailable === true && !!pokEnvironment;

  if (!hasStudio) {
    return (
      <div className="min-h-screen bg-background">
        <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
          <div className="flex items-center gap-2">
            <CreditCard className="h-5 w-5" />
            <span className="font-semibold tracking-tight">Payment Methods</span>
          </div>
        </header>
        <main className="max-w-2xl mx-auto px-4 py-6">
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <CreditCard className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
            <div className="space-y-1">
              <p className="text-sm font-medium">You haven&apos;t joined a studio yet</p>
              <p className="text-xs text-muted-foreground max-w-xs">
                A saved card belongs to a specific studio. Browse studios to get started.
              </p>
            </div>
            <Button asChild size="sm" className="bg-violet-600 hover:bg-violet-700 text-white">
              <Link to="/discover">Browse studios</Link>
            </Button>
          </div>
        </main>
      </div>
    );
  }

  async function handleAddCardSuccess(data: AddCardData) {
    const result = await addMethod({
      jwe: data.csFlexCard.jwe,
      securityCode: data.securityCode ?? null,
      firstName: data.billingInfo.firstName,
      lastName: data.billingInfo.lastName,
      email: data.billingInfo.email,
      countryCode: data.billingInfo.countryCode,
      administrativeArea: data.billingInfo.administrativeArea ?? null,
      locality: data.billingInfo.locality ?? null,
      address1: data.billingInfo.address1 ?? null,
      postalCode: data.billingInfo.postalCode ?? null,
      phoneNumber: data.billingInfo.phoneNumber ?? null,
    });

    if ("data" in result) {
      toast.success("Card saved.");
      // Remounts AddCardForm so it resets to a fresh, empty state for the next card.
      setFormKey((k) => k + 1);
    } else {
      toast.error("Failed to save card. Please try again.");
    }
  }

  function handleAddCardError(error: PaymentErrorResponse) {
    toast.error(error.message ?? "Failed to save card. Please try again.");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Payment Methods</span>
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-6">
        <div className="space-y-2">
          {isLoading && (
            <div className="space-y-3" aria-label="Loading saved payment methods">
              {Array.from({ length: 2 }).map((_, i) => (
                <Skeleton key={i} className="h-16 w-full rounded-lg" />
              ))}
            </div>
          )}

          {isError && (
            <div className="flex flex-col items-center justify-center gap-2 py-8 text-center">
              <p className="text-sm text-destructive-text">Failed to load saved payment methods.</p>
              <button
                type="button"
                onClick={() => refetch()}
                className="text-xs underline text-muted-foreground hover:text-foreground"
              >
                Try again
              </button>
            </div>
          )}

          {!isLoading && !isError && methods?.length === 0 && (
            <p className="text-sm text-muted-foreground text-center py-4">
              No saved cards yet — add one below to speed up checkout next time.
            </p>
          )}

          {!isLoading && !isError && methods?.map((method) => (
            <CardRow key={method.id} method={method} />
          ))}
        </div>

        <div className="space-y-2">
          <h2 className="text-sm font-semibold">Add a card</h2>

          {isLoadingCapabilities && (
            <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span className="text-sm">Preparing form…</span>
            </div>
          )}

          {!isLoadingCapabilities && !cardsAvailable && (
            <p className="text-sm text-destructive-text py-4 text-center">
              Card payments are temporarily unavailable — try again later.
            </p>
          )}

          {!isLoadingCapabilities && cardsAvailable && (
            <div className="space-y-2">
              {isAdding ? (
                <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
                  <Loader2 className="h-4 w-4 animate-spin" />
                  <span className="text-sm">Saving card…</span>
                </div>
              ) : (
                <AddCardForm
                  key={formKey}
                  buttonTitle="Save card"
                  onSuccess={(data) => void handleAddCardSuccess(data)}
                  onError={handleAddCardError}
                  options={{ env: pokEnvironment as "staging" | "production", locale: "en", countrySelect: "modal" }}
                />
              )}
              <p className="text-xs text-center text-muted-foreground">
                Your card details are handled entirely by our payment processor — we never see
                or store your card number.
              </p>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
