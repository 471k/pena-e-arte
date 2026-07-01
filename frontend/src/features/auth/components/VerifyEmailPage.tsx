import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { CheckCircle2, Loader2, XCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export function VerifyEmailPage() {
  const [searchParams]          = useSearchParams();
  const navigate                = useNavigate();
  const [status, setStatus]     = useState<"loading" | "success" | "error">("loading");

  useEffect(() => {
    // The backend redirects GET /api/v1/auth/verify-email?userId=&token= to /login?verified=true
    // This page handles the /verify-email client-side route shown in confirmation emails.
    const userId = searchParams.get("userId");
    const token  = searchParams.get("token");

    if (!userId || !token) {
      setStatus("error");
      return;
    }

    const url = `/api/v1/auth/verify-email?userId=${encodeURIComponent(userId)}&token=${encodeURIComponent(token)}`;

    fetch(url, { method: "GET", redirect: "follow" })
      .then((res) => {
        if (res.ok || res.redirected) {
          setStatus("success");
        } else {
          setStatus("error");
        }
      })
      .catch(() => setStatus("error"));
  }, [searchParams]);

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <div className="max-w-sm w-full text-center space-y-4">
        {status === "loading" && (
          <>
            <Loader2 className="h-10 w-10 animate-spin mx-auto text-muted-foreground" />
            <p className="text-sm text-muted-foreground">Verifying your email…</p>
          </>
        )}
        {status === "success" && (
          <>
            <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" />
            <p className="font-semibold">Email confirmed!</p>
            <p className="text-sm text-muted-foreground">You can now sign in.</p>
            <Button onClick={() => navigate("/login")} className="w-full">Sign in</Button>
          </>
        )}
        {status === "error" && (
          <>
            <XCircle className="h-10 w-10 mx-auto text-destructive" />
            <p className="font-semibold">Verification failed</p>
            <p className="text-sm text-muted-foreground">
              The link may have expired. Request a new one from your account settings.
            </p>
            <Button variant="outline" onClick={() => navigate("/login")} className="w-full">
              Back to login
            </Button>
          </>
        )}
      </div>
    </div>
  );
}
