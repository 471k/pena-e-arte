import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Mail } from "lucide-react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { z } from "zod";
import { Button }   from "@/shared/components/ui/button";
import { Input }    from "@/shared/components/ui/input";
import { Label }    from "@/shared/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { useRequestChangeEmailMutation } from "../authApi";

const schema = z.object({
  currentPassword: z.string().min(1, "Required"),
  newEmail:         z.string().min(1, "Required").email("Enter a valid email address"),
});

type FormValues = z.infer<typeof schema>;

export function RequestChangeEmailPage() {
  const [requestChangeEmail, { isLoading, isSuccess }] = useRequestChangeEmailMutation();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema), mode: "onTouched" });

  async function onSubmit(values: FormValues) {
    const result = await requestChangeEmail(values);
    if ("error" in result) {
      const err = result.error;
      const message =
        err && "status" in err && err.status === 409
          ? "That email is already in use."
          : "Failed to start email change. Check your current password.";
      toast.error(message);
    } else {
      toast.success("Check your new inbox for a confirmation link.");
      reset();
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Mail className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Change Email</span>
      </header>

      <main className="max-w-md mx-auto px-4 py-8">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Update your email address</CardTitle>
          </CardHeader>
          <CardContent>
            {isSuccess ? (
              <div className="space-y-2 text-sm text-muted-foreground">
                <p>We sent a confirmation link to your new email address.</p>
                <p>Click it to finish the change — your current email stays active until then.</p>
              </div>
            ) : (
              <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
                <div className="space-y-1.5">
                  <Label htmlFor="currentPassword">Current password</Label>
                  <PasswordInput
                    id="currentPassword"
                    autoComplete="current-password"
                    {...register("currentPassword")}
                    aria-invalid={!!errors.currentPassword}
                    aria-describedby={errors.currentPassword ? "cur-pw-err" : undefined}
                  />
                  {errors.currentPassword && (
                    <p id="cur-pw-err" className="text-xs text-destructive-text" role="alert">
                      {errors.currentPassword.message}
                    </p>
                  )}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="newEmail">New email address</Label>
                  <Input
                    id="newEmail"
                    type="email"
                    autoComplete="email"
                    {...register("newEmail")}
                    aria-invalid={!!errors.newEmail}
                    aria-describedby={errors.newEmail ? "new-email-err" : undefined}
                  />
                  {errors.newEmail && (
                    <p id="new-email-err" className="text-xs text-destructive-text" role="alert">
                      {errors.newEmail.message}
                    </p>
                  )}
                </div>

                <Button type="submit" className="w-full" disabled={isLoading}>
                  {isLoading ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : null}
                  Send confirmation link
                </Button>
              </form>
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
