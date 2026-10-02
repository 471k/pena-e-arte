import { Link, useSearchParams } from "react-router-dom";
import { CheckCircle2 } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

// The status page Meta shows (and links to) after it sends our Data Deletion callback. The
// erasure itself happens synchronously inside that callback (POST /api/v1/instagram/data-deletion),
// so by the time anyone lands here the deletion is already complete — this page only confirms it
// and echoes the confirmation code. The code is random and not stored, so nothing here can be
// used to look up anyone's data.
export function InstagramDataDeletionPage() {
  useDocumentMeta({
    title: "Instagram data deletion — TattooOS",
    canonical: "/data-deletion/instagram",
  });

  const [searchParams] = useSearchParams();
  const code = searchParams.get("code");

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <main className="max-w-md w-full space-y-4 text-center">
        <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" aria-hidden="true" />
        <h1 className="text-xl font-semibold tracking-tight">Instagram data deletion</h1>

        {code ? (
          <>
            <p className="text-sm text-muted-foreground">
              Your request was received and completed. TattooOS deleted the Instagram access token
              and the synced posts it held for your account, and stopped showing them on the
              artist&apos;s public portfolio.
            </p>
            <p className="text-sm">
              Confirmation code:{" "}
              <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">{code}</code>
            </p>
          </>
        ) : (
          <p className="text-sm text-muted-foreground">
            When you remove TattooOS in Instagram under Settings &rarr; Apps and websites, Instagram
            tells us and we immediately delete the access token and synced posts we held for your
            account. You can also disconnect Instagram from your artist profile in TattooOS, which
            does the same.
          </p>
        )}

        <p className="text-sm text-muted-foreground">
          Questions, or want other data deleted? See our{" "}
          <Link to="/privacy" className="underline underline-offset-2 hover:text-foreground">
            Privacy Policy
          </Link>{" "}
          or{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            contact us
          </Link>
          .
        </p>
      </main>
    </div>
  );
}
