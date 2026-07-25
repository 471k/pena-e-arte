import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { CheckCircle2, Loader2, XCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export function ConfirmChangeEmailPage() {
  const [searchParams]          = useSearchParams();
  const navigate                = useNavigate();
  const [status, setStatus]     = useState<"loading" | "success" | "error">("loading");

  useEffect(() => {
    // The backend redirects GET /api/v1/auth/confirm-change-email?userId=&newEmail=&token=
    // to /login?email-changed=true — this page handles the /confirm-change-email
    // client-side route linked from the confirmation email.
    const userId   = searchParams.get("userId");
    const newEmail = searchParams.get("newEmail");
    const token    = searchParams.get("token");

    if (!userId || !newEmail || !token) {
      setStatus("error");
      return;
    }

    const url = `/api/v1/auth/confirm-change-email` +
      `?userId=${encodeURIComponent(userId)}` +
      `&newEmail=${encodeURIComponent(newEmail)}` +
      `&token=${encodeURIComponent(token)}`;

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
            <p className="text-sm text-muted-foreground">Confirming your new email…</p>
          </>
        )}
        {status === "success" && (
          <>
            <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" />
            <p className="font-semibold">Email changed!</p>
            <p className="text-sm text-muted-foreground">Sign in with your new email address.</p>
            <Button onClick={() => navigate("/login")} className="w-full">Sign in</Button>
          </>
        )}
        {status === "error" && (
          <>
            <XCircle className="h-10 w-10 mx-auto text-destructive" />
            <p className="font-semibold">Confirmation failed</p>
            <p className="text-sm text-muted-foreground">
              The link may have expired or already been used. Request a new one from your account settings.
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
