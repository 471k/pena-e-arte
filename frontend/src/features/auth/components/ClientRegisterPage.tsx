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
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { decodeToken } from "@/shared/utils/jwt";
import { useLoginMutation, useRegisterUserMutation } from "../authApi";
import { setCredentials } from "../authSlice";

const schema = z
  .object({
    firstName:       z.string().min(1, "First name is required").max(100),
    email:           z.string().min(1, "Email is required").email("Enter a valid email"),
    password:        z.string().min(8, "Password must be at least 8 characters"),
    confirmPassword: z.string().min(1, "Confirm your password"),
  })
  .superRefine((data, ctx) => {
    if (data.password !== data.confirmPassword) {
      ctx.addIssue({
        code:    "custom",
        message: "Passwords do not match",
        path:    ["confirmPassword"],
      });
    }
  });

type FormValues = z.infer<typeof schema>;

export function ClientRegisterPage() {
  const dispatch   = useAppDispatch();
  const navigate   = useNavigate();
  const [params]   = useSearchParams();
  const existingRole = useAppSelector((s) => s.auth.role);

  const studioId   = params.get("studioId") ?? "";
  const redirectTo = params.get("redirect") ?? "/book";

  const [registerUser, { isLoading: isRegistering, error: registerError }] =
    useRegisterUserMutation();
  const [login, { isLoading: isLoggingIn }] = useLoginMutation();

  const isLoading = isRegistering || isLoggingIn;

  useEffect(() => {
    if (existingRole) navigate(getRoleRedirectPath(existingRole), { replace: true });
  }, [existingRole, navigate]);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    await registerUser({
      email:     values.email,
      password:  values.password,
      role:      "client",
      studioId,
      firstName: values.firstName,
    }).unwrap();

    const { accessToken } = await login({
      email:    values.email,
      password: values.password,
    }).unwrap();

    dispatch(setCredentials(decodeToken(accessToken)));
    navigate(redirectTo, { replace: true });
  }

  const serverError = registerError
    ? "data" in registerError
      ? registerError.status === 429
        ? "Too many attempts. Please try again in a few minutes."
        : (registerError.data as { message?: string; detail?: string })?.message ??
          (registerError.data as { message?: string; detail?: string })?.detail ??
          "Registration failed. Please try again."
      : "Unable to reach the server. Please try again."
    : null;

  if (!studioId) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-4">
        <div className="w-full max-w-md space-y-6 text-center">
          <div className="flex flex-col items-center gap-2">
            <PenLine className="h-8 w-8" aria-hidden="true" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Artë</span>
          </div>
          <Card className="dark:bg-zinc-900/80 dark:border-zinc-800">
            <CardContent className="pt-6 space-y-4">
              <p className="text-sm text-muted-foreground">
                To create a client account you need to start from a studio's
                booking page so we know which studio to link you to.
              </p>
              <Button asChild className="w-full bg-violet-600 hover:bg-violet-700 text-white border-0">
                <Link to="/discover">Browse studios</Link>
              </Button>
            </CardContent>
          </Card>
          <p className="text-sm text-muted-foreground">
            Already have an account?{" "}
            <Link
              to="/login"
              className="underline underline-offset-4 hover:text-foreground"
            >
              Sign in
            </Link>
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4 relative overflow-hidden">
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          background:
            "radial-gradient(ellipse 90% 55% at 50% -5%, rgba(113,113,122,0.18) 0%, transparent 100%)",
        }}
        aria-hidden="true"
      />

      <div className="w-full max-w-md space-y-6 relative">
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" aria-hidden="true" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Artë</span>
          </div>
          <p className="text-sm text-foreground/65">
            Create a client account to book appointments.
          </p>
        </div>

        <Card className="dark:bg-zinc-900/80 dark:border-zinc-800 shadow-lg dark:shadow-black/60">
          <CardHeader>
            <CardTitle>Create your account</CardTitle>
            <CardDescription>Free client account — book, track, and manage your appointments.</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
              <div className="space-y-1.5">
                <Label htmlFor="firstName">First name</Label>
                <Input
                  id="firstName"
                  type="text"
                  autoComplete="given-name"
                  placeholder="Alex"
                  {...register("firstName")}
                  aria-invalid={!!errors.firstName}
                  aria-describedby={errors.firstName ? "firstName-error" : undefined}
                />
                {errors.firstName && (
                  <p id="firstName-error" className="text-xs text-destructive" role="alert">
                    {errors.firstName.message}
                  </p>
                )}
              </div>

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

              <div className="space-y-1.5">
                <Label htmlFor="password">Password</Label>
                <PasswordInput
                  id="password"
                  autoComplete="new-password"
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
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="confirmPassword">Confirm password</Label>
                <PasswordInput
                  id="confirmPassword"
                  autoComplete="new-password"
                  placeholder="••••••••"
                  {...register("confirmPassword")}
                  aria-invalid={!!errors.confirmPassword}
                  aria-describedby={errors.confirmPassword ? "confirmPassword-error" : undefined}
                />
                {errors.confirmPassword && (
                  <p id="confirmPassword-error" className="text-xs text-destructive" role="alert">
                    {errors.confirmPassword.message}
                  </p>
                )}
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
                Create account
              </Button>
            </form>

            <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-foreground/65">
              Already have an account?{" "}
              <Link
                to={`/login${redirectTo !== "/book" ? `?redirect=${encodeURIComponent(redirectTo)}` : ""}`}
                className="underline underline-offset-4 text-foreground/65 hover:text-foreground py-2 inline-block"
              >
                Sign in
              </Link>
            </div>
          </CardContent>
        </Card>

        <p className="text-center text-sm text-foreground/40">
          Registering a studio instead?{" "}
          <Link
            to="/register"
            className="underline underline-offset-4 hover:text-foreground/70"
          >
            Register your studio
          </Link>
        </p>
      </div>
    </div>
  );
}
