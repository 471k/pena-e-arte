import { zodResolver } from "@hookform/resolvers/zod";
import { CheckCircle, Loader2, PenLine } from "lucide-react";
import { useForm } from "react-hook-form";
import { Link, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useResetPasswordMutation } from "../authApi";

const schema = z.object({
  email:       z.string().min(1, "Email is required").email("Enter a valid email"),
  token:       z.string().min(1, "Reset token is required"),
  newPassword: z.string().min(8, "Password must be at least 8 characters"),
  confirm:     z.string().min(1, "Please confirm your password"),
}).refine((d) => d.newPassword === d.confirm, {
  path: ["confirm"],
  message: "Passwords do not match",
});

type FormValues = z.infer<typeof schema>;

export function ResetPasswordPage() {
  const [searchParams]  = useSearchParams();
  const [resetPassword, { isLoading, isSuccess, error }] = useResetPasswordMutation();

  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: searchParams.get("email") ?? "",
      token: searchParams.get("token") ?? "",
    },
  });

  async function onSubmit(values: FormValues) {
    try {
      await resetPassword({
        email:       values.email,
        token:       values.token,
        newPassword: values.newPassword,
      }).unwrap();
    } catch {
      // surfaced via error state
    }
  }

  const serverError = error
    ? "data" in error
      ? (error.data as { message?: string })?.message ?? "Reset failed."
      : "Unable to reach the server."
    : null;

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div className="w-full max-w-md space-y-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex items-center gap-2">
            <PenLine className="h-8 w-8" />
            <span className="text-2xl font-semibold tracking-tight">Pena e Arte</span>
          </div>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>Set new password</CardTitle>
            <CardDescription>Enter your reset token and choose a new password.</CardDescription>
          </CardHeader>
          <CardContent>
            {isSuccess ? (
              <div className="flex flex-col items-center gap-3 py-4 text-center">
                <CheckCircle className="h-8 w-8 text-green-500" />
                <p className="text-sm">Password reset successfully.</p>
                <Link to="/login" className="text-sm underline underline-offset-4 hover:text-primary">
                  Sign in
                </Link>
              </div>
            ) : (
              <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
                <div className="space-y-1.5">
                  <Label htmlFor="email">Email</Label>
                  <Input id="email" type="email" autoComplete="email" {...register("email")} aria-invalid={!!errors.email} />
                  {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="token">Reset token</Label>
                  <Input id="token" type="text" {...register("token")} aria-invalid={!!errors.token} />
                  {errors.token && <p className="text-xs text-destructive">{errors.token.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="newPassword">New password</Label>
                  <Input id="newPassword" type="password" autoComplete="new-password" {...register("newPassword")} aria-invalid={!!errors.newPassword} />
                  {errors.newPassword && <p className="text-xs text-destructive">{errors.newPassword.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="confirm">Confirm password</Label>
                  <Input id="confirm" type="password" autoComplete="new-password" {...register("confirm")} aria-invalid={!!errors.confirm} />
                  {errors.confirm && <p className="text-xs text-destructive">{errors.confirm.message}</p>}
                </div>

                {serverError && (
                  <p className="text-sm text-destructive" role="alert">{serverError}</p>
                )}

                <Button type="submit" className="w-full" disabled={isLoading}>
                  {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Reset password
                </Button>
              </form>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
