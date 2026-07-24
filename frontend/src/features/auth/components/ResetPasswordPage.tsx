import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, CheckCircle, Loader2, Pencil, PenLine } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { AuthShellFooter } from "@/shared/components/AuthShellFooter";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { FieldHint } from "@/shared/components/ui/field-hint";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { PasswordMatchIndicator } from "@/shared/components/ui/password-match-indicator";
import { useResetPasswordMutation } from "../authApi";

const schema = z.object({
  email:       z.string().min(1, "Email is required").email("Enter a valid email"),
  token:       z.string().min(1, "Reset token is required"),
  newPassword: z.string()
    .min(8, "Password must be at least 8 characters")
    .regex(/[A-Z]/, "Needs an uppercase letter")
    .regex(/[a-z]/, "Needs a lowercase letter")
    .regex(/[0-9]/, "Needs a digit"),
  confirm:     z.string().min(1, "Please confirm your password"),
}).refine((d) => d.newPassword === d.confirm, {
  path: ["confirm"],
  message: "Passwords do not match",
});

type FormValues = z.infer<typeof schema>;

interface ErrorData {
  message?: string;
  code?: string;
}

export function ResetPasswordPage() {
  const [searchParams]  = useSearchParams();
  const [resetPassword, { isLoading, isSuccess, error }] = useResetPasswordMutation();

  const prefilledEmail = searchParams.get("email") ?? "";
  const prefilledToken = searchParams.get("token") ?? "";
  const [emailEditable, setEmailEditable] = useState(prefilledEmail === "");
  const [tokenEditable, setTokenEditable] = useState(prefilledToken === "");

  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    mode:     "onTouched",
    defaultValues: {
      email: prefilledEmail,
      token: prefilledToken,
    },
  });

  const email       = watch("email");
  const token       = watch("token");
  const newPassword = watch("newPassword");
  const confirm     = watch("confirm");

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

  const errorData = error && "data" in error ? (error.data as ErrorData) : null;
  const isTokenError = errorData?.code === "RESET_TOKEN_INVALID";
  const serverError = error
    ? isTokenError
      ? "This reset link is invalid or has expired."
      : errorData
        ? errorData.message ?? "Reset failed."
        : "Unable to reach the server. Please try again."
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
            <CardTitle>Reset your password</CardTitle>
            <CardDescription>
              Choose a new password. Reset links expire 1 hour after they're sent.
            </CardDescription>
          </CardHeader>
          <CardContent>
            {isSuccess ? (
              <div className="flex flex-col items-center gap-3 py-4 text-center">
                <CheckCircle className="h-8 w-8 text-green-500" />
                <p className="text-sm">Password reset successfully.</p>
              </div>
            ) : (
              <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
                <div className="space-y-1.5">
                  <Label htmlFor="email">Email</Label>
                  {emailEditable ? (
                    <Input id="email" type="email" autoComplete="email" {...register("email")} aria-invalid={!!errors.email} />
                  ) : (
                    <div className="flex items-center gap-2">
                      <Input id="email" type="email" readOnly {...register("email")} className="bg-muted cursor-default" />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="min-h-[44px] min-w-[44px]"
                        aria-label="Change email"
                        onClick={() => setEmailEditable(true)}
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                    </div>
                  )}
                  {emailEditable && (
                    <FieldHint>This is the email your reset link was sent to.</FieldHint>
                  )}
                  {errors.email && <p className="text-xs text-destructive-text">{errors.email.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="token">Reset token</Label>
                  {tokenEditable ? (
                    <>
                      <Input
                        id="token"
                        type="text"
                        autoComplete="off"
                        className="font-mono"
                        {...register("token")}
                        aria-invalid={!!errors.token}
                      />
                      <FieldHint>{token.length} character{token.length === 1 ? "" : "s"} entered.</FieldHint>
                    </>
                  ) : (
                    <div className="flex items-center gap-2">
                      <Input
                        id="token"
                        type="text"
                        readOnly
                        className="font-mono bg-muted cursor-default"
                        {...register("token")}
                      />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="min-h-[44px] min-w-[44px]"
                        aria-label="Change reset token"
                        onClick={() => setTokenEditable(true)}
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                    </div>
                  )}
                  {errors.token && <p className="text-xs text-destructive-text">{errors.token.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="newPassword">New password</Label>
                  <PasswordInput id="newPassword" autoComplete="new-password" {...register("newPassword")} aria-invalid={!!errors.newPassword} />
                  <FieldHint>At least 8 characters, with an uppercase letter, a lowercase letter, and a number.</FieldHint>
                  {errors.newPassword && <p className="text-xs text-destructive-text">{errors.newPassword.message}</p>}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="confirm">Confirm password</Label>
                  <PasswordInput id="confirm" autoComplete="new-password" {...register("confirm")} aria-invalid={!!errors.confirm} />
                  <PasswordMatchIndicator password={newPassword ?? ""} confirm={confirm ?? ""} />
                  {errors.confirm && <p className="text-xs text-destructive-text">{errors.confirm.message}</p>}
                </div>

                {serverError && (
                  <Alert variant="destructive" role="alert">
                    <AlertCircle className="h-4 w-4" />
                    <AlertDescription className="space-y-2">
                      <p>{serverError}</p>
                      {isTokenError && (
                        <Link
                          to={`/forgot-password${email ? `?email=${encodeURIComponent(email)}` : ""}`}
                          className="inline-block text-sm font-medium underline underline-offset-4"
                        >
                          Request a new reset link
                        </Link>
                      )}
                    </AlertDescription>
                  </Alert>
                )}

                <Button type="submit" className="w-full" disabled={isLoading}>
                  {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Reset password
                </Button>
              </form>
            )}

            <AuthShellFooter>
              <Link to="/login" className="underline underline-offset-4 hover:text-primary py-2 inline-block">
                Back to sign in
              </Link>
            </AuthShellFooter>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
