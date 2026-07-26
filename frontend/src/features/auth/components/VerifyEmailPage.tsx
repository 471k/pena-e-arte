import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { CheckCircle2, Loader2, XCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { decodeToken } from "@/shared/utils/jwt";
import { useRefreshTokenMutation } from "../authApi";
import { setCredentials } from "../authSlice";

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const navigate       = useNavigate();

  // The backend redirects GET /api/v1/auth/verify-email?userId=&token= to /login?verified=true
  // This page handles the /verify-email client-side route shown in confirmation emails.
  const userId = searchParams.get("userId");
  const token  = searchParams.get("token");

  // Missing params is knowable synchronously from the URL at render time, so the
  // initial state reflects it directly instead of starting at "loading" and
  // setting state from inside the effect.
  const [status, setStatus] = useState<"loading" | "success" | "error">(
    userId && token ? "loading" : "error",
  );

  const dispatch = useAppDispatch();
  // If this browser tab already holds a session (e.g. the user was auto-signed-in
  // right after registering, before verifying), that session's JWT still carries
  // email_verified=false. Clicking "Sign in" below won't fix it — the user is
  // already authenticated, so LoginPage just redirects them without requesting a
  // fresh token. Refresh it here instead, the moment confirmation succeeds.
  const existingRefreshToken = useAppSelector((s) => s.auth.refreshToken);
  const [refreshToken] = useRefreshTokenMutation();

  useEffect(() => {
    if (!userId || !token) return;

    const url = `/api/v1/auth/verify-email?userId=${encodeURIComponent(userId)}&token=${encodeURIComponent(token)}`;

    fetch(url, { method: "GET", redirect: "follow" })
      .then((res) => {
        if (res.ok || res.redirected) {
          setStatus("success");
          if (existingRefreshToken) {
            refreshToken({ refreshToken: existingRefreshToken })
              .unwrap()
              .then(({ accessToken, refreshToken: newRefreshToken }) => {
                const payload = decodeToken(accessToken);
                dispatch(setCredentials({ ...payload, refreshToken: newRefreshToken }));
              })
              .catch(() => {
                // Best-effort — if this fails the user still gets a correct token
                // the next time they actually sign in with credentials.
              });
          }
        } else {
          setStatus("error");
        }
      })
      .catch(() => setStatus("error"));
    // Deliberately excludes existingRefreshToken/refreshToken/dispatch — this must
    // only run once per confirmation link, not re-fire as the token itself updates.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, token]);

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
