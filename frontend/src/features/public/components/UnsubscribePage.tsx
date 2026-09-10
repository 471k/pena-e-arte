import { useSearchParams } from "react-router-dom";
import { CheckCircle2, Loader2, XCircle } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useWithdrawMarketingOptInQuery } from "@/features/public/marketingApi";

export function UnsubscribePage() {
  useDocumentMeta({ title: "Unsubscribe — TattooOS", canonical: "/unsubscribe" });

  const [searchParams] = useSearchParams();
  const token = searchParams.get("token");

  const { isLoading, isSuccess, isError } = useWithdrawMarketingOptInQuery(token ?? "", {
    skip: !token,
  });

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <div className="max-w-sm w-full text-center space-y-4">
        {(!token || isError) && (
          <>
            <XCircle className="h-10 w-10 mx-auto text-destructive" />
            <p className="font-semibold">This unsubscribe link is invalid or has expired</p>
          </>
        )}
        {token && isLoading && (
          <>
            <Loader2 className="h-10 w-10 animate-spin mx-auto text-muted-foreground" />
            <p className="text-sm text-muted-foreground">Unsubscribing…</p>
          </>
        )}
        {isSuccess && (
          <>
            <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" />
            <p className="font-semibold">You've been unsubscribed</p>
            <p className="text-sm text-muted-foreground">
              You won't receive marketing emails from this studio anymore.
            </p>
          </>
        )}
      </div>
    </div>
  );
}
