import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, Loader2 } from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { getRoleRedirectPath } from "@/app/router";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { OAuthButtons } from "@/shared/components/OAuthButtons";
import { GuestAuthHeader } from "@/shared/components/GuestAuthHeader";
import { decodeToken } from "@/shared/utils/jwt";
import { useLoginMutation, useOauthLoginMutation } from "../authApi";
import { setCredentials } from "../authSlice";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { TattooOSLogo } from "@/shared/components/TattooOSLogo";

const loginSchema = z.object({
  email:    z.string().min(1, "Email is required").email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type LoginFormValues = z.infer<typeof loginSchema>;

/**
 * Hides a footer separator dot when the links either side of it sit on different lines, so a wrapped footer never
 * ends a line, or starts one, with a dangling "·" (seen at 280-430px, and at 200% text up to three per footer).
 * Only `visibility` changes: the dot keeps its space, so hiding it can never change where the row wraps.
 * Approved useLayoutEffect: it measures layout, it fetches nothing.
 */
function useHideWrappedSeparators(rowRef: RefObject<HTMLElement | null>) {
  useLayoutEffect(() => {
    const row = rowRef.current;
    if (!row) return;
    const separators = Array.from(row.querySelectorAll<HTMLElement>(":scope > span[aria-hidden='true']"));

    function update() {
      for (const dot of separators) {
        const before = dot.previousElementSibling?.getBoundingClientRect();
        const after = dot.nextElementSibling?.getBoundingClientRect();
        // A later line starts at or below the bottom of the earlier one; 1px absorbs sub-pixel rounding.
        const wrapped = !before || !after || after.top >= before.bottom - 1;
        dot.style.visibility = wrapped ? "hidden" : "";
      }
    }

    update();
    if (typeof ResizeObserver === "undefined") return;
    // The row's size changes when the window or the text size does; each link's size changes when the font loads.
    const observer = new ResizeObserver(update);
    observer.observe(row);
    for (const child of Array.from(row.children)) observer.observe(child);
    return () => observer.disconnect();
  }, [rowRef]);
}

export function LoginPage() {
  useDocumentMeta({
    title:       "Sign in — TattooOS",
    description: "Sign in to manage your tattoo studio appointments, clients, and more.",
    canonical:   `${window.location.origin}/login`,
  });

  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const footerLinksRef = useRef<HTMLDivElement>(null);
  useHideWrappedSeparators(footerLinksRef);
  const [searchParams] = useSearchParams();
  const existingRole = useAppSelector((s) => s.auth.role);
  const [remember, setRemember] = useState(true);
  const [login, { isLoading, error }] = useLoginMutation();
  const [oauthLogin] = useOauthLoginMutation();

  const sessionExpired  = searchParams.get("reason") === "session_expired";
  const studioId        = searchParams.get("studioId") ?? "";
  const redirectRaw     = searchParams.get("redirect") ?? "";
  // Only accept same-origin relative paths — a redirect param must never be handed
  // straight to navigate() as an absolute/external URL (open-redirect).
  const redirectParam   = redirectRaw.startsWith("/") && !redirectRaw.startsWith("//") ? redirectRaw : "";
  const redirectPath    = existingRole
    ? (redirectParam || getRoleRedirectPath(existingRole))
    : null;

  // Client registration is studio-less — a "sign up" link must always be offered,
  // with studioId/redirect appended only when present.
  const clientRegisterUrl =
    `/client-register${studioId ? `?studioId=${studioId}` : ""}` +
    (redirectParam
      ? `${studioId ? "&" : "?"}redirect=${encodeURIComponent(redirectParam)}`
      : "");

  // Handles both "already logged in" and "just logged in" cases — fires once
  // existingRole is set. Respects ?redirect= so post-auth deep-links work.
  useEffect(() => {
    if (redirectPath) navigate(redirectPath, { replace: true });
  }, [redirectPath, navigate]);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(values: LoginFormValues) {
    try {
      const { accessToken, refreshToken } = await login(values).unwrap();
      const payload = decodeToken(accessToken);
      dispatch(setCredentials({ ...payload, refreshToken, remember }));
      // Navigation handled by the useEffect above once Redux re-renders with the new role
    } catch {
      // error surfaced via RTK Query's `error` state below
    }
  }

  async function handleOAuthToken({
    provider,
    idToken,
  }: {
    provider: "google" | "apple";
    idToken: string;
  }) {
    const { accessToken, refreshToken } = await oauthLogin({ provider, idToken }).unwrap();
    const payload = decodeToken(accessToken);
    dispatch(setCredentials({ ...payload, refreshToken, remember }));
    // Navigation handled by the useEffect above once Redux re-renders with the new role
  }

  const serverError = error
    ? "data" in error
      ? error.status === 429
        ? "Too many sign-in attempts. Please try again in a few minutes."
        : (error.data as { message?: string; detail?: string })?.message ??
          (error.data as { message?: string; detail?: string })?.detail ??
          "Invalid email or password."
      : "Unable to reach the server. Please try again."
    : null;

  return (
    <div className="min-h-screen flex flex-col bg-background">
      <GuestAuthHeader />

      <div className="flex-1 flex items-center justify-center p-4 relative overflow-hidden">
        {/* Decorative background glow — purely visual */}
        <div
          className="pointer-events-none absolute inset-0"
          style={{
            background:
              "radial-gradient(ellipse 80% 50% at 50% 0%, rgba(124,58,237,0.10) 0%, transparent 70%)",
          }}
          aria-hidden="true"
        />

        <div className="w-full max-w-md space-y-6 relative">
          {/* Brand mark */}
          <div className="flex flex-col items-center gap-2 text-center">
            {/* Brand master 1.0.0 header lockup (189x32). m-2 keeps the package's 8px clear space. */}
            <TattooOSLogo variant="header" className="m-2" />
            <p className="text-sm text-foreground/80">
              Run your studio. Book clients. Manage your team.
            </p>
          </div>

          {sessionExpired && (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-warning-text dark:text-amber-400">
              Your session expired. Please sign in again.
            </div>
          )}

          <Card className="dark:bg-zinc-900/80 dark:border-zinc-700/60 shadow-lg
                   dark:shadow-[0_8px_32px_rgba(255,255,255,0.05)]">
            <CardHeader>
              <CardTitle>Sign in</CardTitle>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
                {/* Email */}
                <div className="space-y-1.5">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    type="email"
                    autoComplete="email"
                    autoFocus
                    placeholder="you@example.com"
                    {...register("email")}
                    aria-invalid={!!errors.email}
                    aria-describedby={errors.email ? "email-error" : undefined}
                  />
                  {errors.email && (
                    <p id="email-error" className="text-xs text-destructive-text" role="alert">
                      {errors.email.message}
                    </p>
                  )}
                </div>

                {/* Password */}
                <div className="space-y-1.5">
                  <Label htmlFor="password">Password</Label>
                  <PasswordInput
                    id="password"
                    autoComplete="current-password"
                    placeholder="••••••••"
                    {...register("password")}
                    aria-invalid={!!errors.password}
                    aria-describedby={errors.password ? "password-error" : undefined}
                  />
                  {errors.password && (
                    <p id="password-error" className="text-xs text-destructive-text" role="alert">
                      {errors.password.message}
                    </p>
                  )}
                  <div className="flex items-center justify-between gap-4 pt-0.5">
                    <label className="flex items-center gap-2 cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={remember}
                        onChange={(e) => setRemember(e.target.checked)}
                        className="h-4 w-4 rounded border-input accent-violet-600 cursor-pointer"
                        aria-label="Remember me on this device"
                      />
                      <span className="text-xs text-foreground/70">Remember me</span>
                    </label>
                    <Link
                      to="/forgot-password"
                      className="text-xs text-foreground/65 underline underline-offset-4
                                 hover:text-foreground py-2 inline-block"
                    >
                      Forgot password?
                    </Link>
                  </div>
                </div>

                {serverError && (
                  <Alert variant="destructive" role="alert">
                    <AlertCircle className="h-4 w-4" />
                    <AlertDescription>{serverError}</AlertDescription>
                  </Alert>
                )}

                <Button
                  type="submit"
                  className="w-full bg-violet-600 hover:bg-violet-700 text-white border-0 focus-visible:ring-violet-500"
                  disabled={isLoading}
                >
                  {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Sign in
                </Button>
              </form>

              <div className="mt-4">
                <OAuthButtons onToken={handleOAuthToken} disabled={isLoading} />
              </div>

              <div className="mt-4 text-center text-sm text-foreground/65">
                Don't have an account?{" "}
                <Link
                  to={clientRegisterUrl}
                  className="underline underline-offset-4 text-violet-700 dark:text-violet-400 hover:text-violet-800 dark:hover:text-violet-300 py-2 inline-block"
                >
                  Sign up
                </Link>
              </div>
              <div className="mt-1 text-center text-xs text-foreground/65">
                Registering a studio instead?{" "}
                <Link
                  to="/register"
                  className="underline underline-offset-4 hover:text-foreground/70"
                >
                  Register your studio
                </Link>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>

      {/* Legal footer, in normal flow as the last child of the page column. flex-1 on the
          content above pushes it to the bottom of a tall viewport; on a short or zoomed one it
          sits below the card and the page scrolls. It was absolute (bottom-6) inside the
          centred container, and at 320-375px its wrapped links overlapped "Register your
          studio", so a tap there opened the wrong legal document (27 Nov 2026). Do not put it
          back inside the overflow-hidden container: there the overlap would become clipping. */}
      <footer className="px-4 pb-6 text-center text-xs text-foreground/65">
        <div ref={footerLinksRef} className="flex flex-wrap gap-x-4 gap-y-1.5 justify-center">
          <a href="/privacy" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
            Privacy Policy
          </a>
          <span aria-hidden="true" className="text-border select-none">·</span>
          <a href="/terms" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
            Studio Terms
          </a>
          <span aria-hidden="true" className="text-border select-none">·</span>
          <a href="/client-terms" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
            Client Terms
          </a>
          <span aria-hidden="true" className="text-border select-none">·</span>
          <a href="mailto:support@tattooos.co" className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline">
            Contact support
          </a>
        </div>
      </footer>
    </div>
  );
}
