import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Lock } from "lucide-react";
import { useForm } from "react-hook-form";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { z } from "zod";
import { Button }   from "@/shared/components/ui/button";
import { Input }    from "@/shared/components/ui/input";
import { Label }    from "@/shared/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { PasswordInput } from "@/shared/components/ui/password-input";
import { useChangePasswordMutation } from "../authApi";

const schema = z
  .object({
    currentPassword: z.string().min(1, "Required"),
    newPassword:     z.string().min(8, "At least 8 characters").regex(/[A-Z]/, "Needs uppercase").regex(/[0-9]/, "Needs a digit"),
    confirmPassword: z.string().min(1, "Required"),
  })
  .refine((d) => d.newPassword === d.confirmPassword, {
    message: "Passwords do not match",
    path:    ["confirmPassword"],
  })
  .refine((d) => d.currentPassword !== d.newPassword, {
    message: "New password must differ from current password",
    path:    ["newPassword"],
  });

type FormValues = z.infer<typeof schema>;

export function ChangePasswordPage() {
  const navigate = useNavigate();
  const [changePassword, { isLoading }] = useChangePasswordMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    const result = await changePassword({
      currentPassword: values.currentPassword,
      newPassword:     values.newPassword,
    });
    if ("error" in result) {
      toast.error("Failed to change password. Check your current password.");
    } else {
      toast.success("Password changed.");
      navigate(-1);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Lock className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Change Password</span>
      </header>

      <main className="max-w-md mx-auto px-4 py-8">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Update your password</CardTitle>
          </CardHeader>
          <CardContent>
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
                  <p id="cur-pw-err" className="text-xs text-destructive" role="alert">
                    {errors.currentPassword.message}
                  </p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="newPassword">New password</Label>
                <PasswordInput
                  id="newPassword"
                  autoComplete="new-password"
                  {...register("newPassword")}
                  aria-invalid={!!errors.newPassword}
                  aria-describedby={errors.newPassword ? "new-pw-err" : undefined}
                />
                {errors.newPassword && (
                  <p id="new-pw-err" className="text-xs text-destructive" role="alert">
                    {errors.newPassword.message}
                  </p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="confirmPassword">Confirm new password</Label>
                <Input
                  id="confirmPassword"
                  type="password"
                  autoComplete="new-password"
                  {...register("confirmPassword")}
                  aria-invalid={!!errors.confirmPassword}
                  aria-describedby={errors.confirmPassword ? "confirm-pw-err" : undefined}
                />
                {errors.confirmPassword && (
                  <p id="confirm-pw-err" className="text-xs text-destructive" role="alert">
                    {errors.confirmPassword.message}
                  </p>
                )}
              </div>

              <Button type="submit" className="w-full" disabled={isLoading}>
                {isLoading ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : null}
                Change password
              </Button>
            </form>
          </CardContent>
        </Card>
      </main>
    </div>
  );
}
