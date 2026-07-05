import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, CheckCircle, ClipboardList, Loader2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useCurrentUser } from "@/shared/hooks/useCurrentUser";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetMyAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useSubmitIntakeFormMutation } from "../intakeFormsApi";

const schema = z.object({
  formData:      z.string().min(10, "Please provide at least 10 characters"),
  appointmentId: z.string().optional(),
  fileUrl:       z.string().url("Must be a valid URL").optional().or(z.literal("")),
});

type FormValues = z.infer<typeof schema>;

export function SubmitIntakeFormPage() {
  useDocumentMeta({ title: "Submit Intake Form — Pena e Artë", canonical: "/forms/intake/new" });

  const navigate = useNavigate();
  const user = useCurrentUser();

  const { data: appointments, isLoading: loadingAppts } = useGetMyAppointmentsQuery();
  const relevantAppointments = appointments?.filter(
    (a) => a.status === "Pending" || a.status === "Confirmed",
  );
  const [submitIntakeForm, { isLoading, isSuccess, isError, reset: resetMutation }] =
    useSubmitIntakeFormMutation();

  const {
    register,
    control,
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
            <Textarea
              id="formData"
              rows={6}
              placeholder="List any allergies, skin conditions, medications, or other relevant health information…"
              disabled={isLoading}
              {...register("formData")}
              className={cn("resize-none", errors.formData && "border-destructive")}
            />
            {errors.formData && (
              <p className="text-xs text-destructive">{errors.formData.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="appointmentId">Appointment (optional)</Label>
            <Controller
              control={control}
              name="appointmentId"
              render={({ field }) => (
                <Select
                  disabled={loadingAppts || isLoading}
                  value={field.value ?? ""}
                  onValueChange={(v) => field.onChange(v === "__none__" ? undefined : v)}
                >
                  <SelectTrigger id="appointmentId">
                    <SelectValue
                      placeholder={loadingAppts ? "Loading appointments…" : "Not linked to an appointment"}
                    />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">Not linked to an appointment</SelectItem>
                    {relevantAppointments?.map((a) => (
                      <SelectItem key={a.id} value={a.id}>
                        {new Date(a.date).toLocaleDateString("en-GB", {
                          day: "numeric", month: "short", year: "numeric",
                        })}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {!loadingAppts && relevantAppointments?.length === 0 && (
              <p className="text-xs text-muted-foreground">
                You don't have any upcoming appointments to link this form to.
              </p>
            )}
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
