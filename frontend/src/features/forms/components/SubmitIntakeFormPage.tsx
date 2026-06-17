import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, CheckCircle, ClipboardList, Loader2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useCurrentUser } from "@/shared/hooks/useCurrentUser";
import { cn } from "@/shared/utils/cn";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useSubmitIntakeFormMutation } from "../intakeFormsApi";

const TEXTAREA_CLS = cn(
  "flex min-h-[120px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background placeholder:text-muted-foreground",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50 resize-none"
);

const SELECT_CLS = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50"
);

const schema = z.object({
  formData:      z.string().min(10, "Please provide at least 10 characters"),
  appointmentId: z.string().optional(),
  fileUrl:       z.string().url("Must be a valid URL").optional().or(z.literal("")),
});

type FormValues = z.infer<typeof schema>;

export function SubmitIntakeFormPage() {
  const navigate = useNavigate();
  const user = useCurrentUser();

  const { data: appointments, isLoading: loadingAppts } = useGetAppointmentsQuery({});
  const [submitIntakeForm, { isLoading, isSuccess, isError, reset: resetMutation }] =
    useSubmitIntakeFormMutation();

  const {
    register,
    handleSubmit,
    reset: resetForm,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    if (!user) return;
    const result = await submitIntakeForm({
      clientId:      user.id,
      formData:      values.formData,
      appointmentId: values.appointmentId || null,
      fileUrl:       values.fileUrl || null,
    });
    if ("data" in result) resetForm();
  }

  if (isSuccess) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="text-center space-y-4 px-6">
          <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
          <p className="text-base font-medium">Intake form submitted!</p>
          <p className="text-sm text-muted-foreground">
            Your studio has received your information.
          </p>
          <div className="flex gap-3 justify-center pt-2">
            <Button variant="outline" size="sm" onClick={resetMutation}>
              Submit another
            </Button>
            <Button size="sm" onClick={() => navigate("/book")}>
              Back to booking
            </Button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)} className="gap-1.5">
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div className="flex items-center gap-2">
          <ClipboardList className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Intake Form</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6">
        <p className="text-sm text-muted-foreground mb-6">
          Please share your medical history and any details your artist should know before your session.
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="formData">Medical history &amp; notes</Label>
            <textarea
              id="formData"
              rows={6}
              placeholder="List any allergies, skin conditions, medications, or other relevant health information…"
              disabled={isLoading}
              {...register("formData")}
              className={cn(TEXTAREA_CLS, errors.formData && "border-destructive")}
            />
            {errors.formData && (
              <p className="text-xs text-destructive">{errors.formData.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="appointmentId">Appointment (optional)</Label>
            <select
              id="appointmentId"
              disabled={loadingAppts || isLoading}
              {...register("appointmentId")}
              className={SELECT_CLS}
            >
              <option value="">
                {loadingAppts ? "Loading appointments…" : "Not linked to an appointment"}
              </option>
              {appointments?.map((a) => (
                <option key={a.id} value={a.id}>
                  {new Date(a.date).toLocaleDateString("en-GB", {
                    day: "numeric", month: "short", year: "numeric",
                  })}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="fileUrl">Attachment URL (optional)</Label>
            <Input
              id="fileUrl"
              type="text"
              placeholder="https://…"
              disabled={isLoading}
              {...register("fileUrl")}
              className={cn(errors.fileUrl && "border-destructive")}
            />
            {errors.fileUrl && (
              <p className="text-xs text-destructive">{errors.fileUrl.message}</p>
            )}
          </div>

          {isError && (
            <p className="text-sm text-destructive text-center">
              Failed to submit. Please try again.
            </p>
          )}

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Submitting…
              </>
            ) : (
              "Submit Intake Form"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
