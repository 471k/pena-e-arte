import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, Loader2, PenLine } from "lucide-react";
import { useEffect } from "react";
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
import { decodeToken } from "@/shared/utils/jwt";
import { useLoginMutation } from "../authApi";
import { setCredentials } from "../authSlice";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

const loginSchema = z.object({
  email:    z.string().min(1, "Email is required").email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type LoginFormValues = z.infer<typeof loginSchema>;

export function LoginPage() {
  useDocumentMeta({
    title:       "Sign in — Pena e Artë",
    description: "Sign in to manage your tattoo studio appointments, clients, and more.",
    canonical:   `${window.location.origin}/login`,
  });

  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const existingRole = useAppSelector((s) => s.auth.role);
  const [login, { isLoading, error }] = useLoginMutation();

  const sessionExpired  = searchParams.get("reason") === "session_expired";
  const studioId        = searchParams.get("studioId") ?? "";
  const redirectParam   = searchParams.get("redirect") ?? "";
  const redirectPath    = existingRole
    ? (redirectParam || getRoleRedirectPath(existingRole))
    : null;

  const clientRegisterUrl = studioId
    ? `/client-register?studioId=${studioId}${redirectParam ? `&redirect=${encodeURIComponent(redirectParam)}` : ""}`
    : null;

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
      const { accessToken } = await login(values).unwrap();
      const payload = decodeToken(accessToken);
      dispatch(setCredentials(payload));
      // Navigation handled by the useEffect above once Redux re-renders with the new role
    } catch {
      // error surfaced via RTK Query's `error` state below
    }
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
    <div className="min-h-screen flex items-center justify-center bg-background p-4 relative overflow-hidden">
      {/* Decorative background glow — purely visual */}
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          background:
            "radial-gradient(ellipse 90% 55% at 50% -5%, rgba(113,113,122,0.18) 0%, transparent 100%)",
        }}
        aria-hidden="true"
      />

      <div className="w-full max-w-md space-y-6 relative">
        {/* Brand mark */}
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" aria-hidden="true" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
          </div>
          <p className="text-sm text-foreground/65">
            Run your studio. Book clients. Manage your team.
          </p>
        </div>

        {sessionExpired && (
          <div className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:text-amber-400">
            Your session expired. Please sign in again.
          </div>
        )}

        <Card className="dark:bg-zinc-900/80 dark:border-zinc-800 shadow-lg dark:shadow-black/60">
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
                  placeholder="you@example.com"
                  {...register("email")}
                  aria-invalid={!!errors.email}
                  aria-describedby={errors.email ? "email-error" : undefined}
                />
                {errors.email && (
                  <p id="email-error" className="text-xs text-destructive" role="alert">
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
                  <p id="password-error" className="text-xs text-destructive" role="alert">
                    {errors.password.message}
                  </p>
                )}
                <div className="flex justify-end">
                  <Link
                    to="/forgot-password"
                    className="text-xs text-foreground/65 underline underline-offset-4 hover:text-foreground py-2 inline-block"
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

            <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-foreground/65">
              {clientRegisterUrl ? (
                <>
                  Don't have an account?{" "}
                  <Link
                    to={clientRegisterUrl}
                    className="underline underline-offset-4 text-foreground/65 hover:text-foreground py-2 inline-block"
                  >
                    Create a client account
                  </Link>
                </>
              ) : (
                <>
                  Don't have an account?{" "}
                  <Link
                    to="/register"
                    className="underline underline-offset-4 text-foreground/65 hover:text-foreground py-2 inline-block"
                  >
                    Register your studio
                  </Link>
                </>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Legal footer — pinned to viewport bottom */}
      <footer className="absolute bottom-6 left-0 right-0 text-center text-xs text-foreground/40 space-x-4">
        <a
          href="/privacy"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Privacy Policy
        </a>
        <span aria-hidden="true">·</span>
        <a
          href="/terms"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Terms of Service
        </a>
        <span aria-hidden="true">·</span>
        <a
          href="mailto:support@penaearte.com"
          className="hover:text-foreground/70 transition-colors underline-offset-2 hover:underline"
        >
          Contact support
        </a>
      </footer>
    </div>
  );
}
