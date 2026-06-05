import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, CheckCircle, FileSignature, FileText, Loader2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { useAppSelector } from "@/app/hooks";
import { cn } from "@/shared/utils/cn";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useSignConsentFormMutation } from "../consentFormsApi";
import { FileUploadField, PDF_ACCEPTED_TYPES } from "@/shared/components/FileUploadField";

const TEXTAREA_CLS = cn(
  "flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
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
  appointmentId: z.string().min(1, "Please select an appointment"),
  signatureData: z.string().min(2, "Please type your full name to sign"),
});

type FormValues = z.infer<typeof schema>;

export function SignConsentFormPage() {
  const navigate = useNavigate();
  const user = useAppSelector((s) => s.auth.user);

  const { data: appointments, isLoading: loadingAppts } = useGetAppointmentsQuery({});
  const [signConsentForm, { isLoading, isSuccess, reset: resetMutation }] =
    useSignConsentFormMutation();

  const [pdfUrl, setPdfUrl] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset: resetForm,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    if (!user) return;
    const result = await signConsentForm({
      clientId:      user.id,
      appointmentId: values.appointmentId,
      signatureData: values.signatureData,
      fileUrl:       pdfUrl,
    });
    if ("data" in result) {
      resetForm();
      setPdfUrl(null);
    }
  }

  if (isSuccess) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="text-center space-y-4 px-6">
          <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
          <p className="text-base font-medium">Consent form signed!</p>
          <p className="text-sm text-muted-foreground">
            Your signature has been recorded for this appointment.
          </p>
          <div className="flex gap-3 justify-center pt-2">
            <Button variant="outline" size="sm" onClick={resetMutation}>
              Sign another
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
          <FileSignature className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Consent Form</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6">
        <p className="text-sm text-muted-foreground mb-6">
          By signing this consent form you acknowledge the risks and procedures associated with your
          tattoo session. Type your full legal name below to provide your digital signature.
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="appointmentId">Appointment</Label>
            <select
              id="appointmentId"
              disabled={loadingAppts || isLoading}
              {...register("appointmentId")}
              className={cn(SELECT_CLS, errors.appointmentId && "border-destructive")}
            >
              <option value="">
                {loadingAppts ? "Loading appointments…" : "Select an appointment"}
              </option>
              {appointments?.map((a) => (
                <option key={a.id} value={a.id}>
                  {new Date(a.date).toLocaleDateString("en-GB", {
                    day: "numeric", month: "short", year: "numeric",
                  })}
                </option>
              ))}
            </select>
            {errors.appointmentId && (
              <p className="text-xs text-destructive">{errors.appointmentId.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="signatureData">Digital signature (full name)</Label>
            <textarea
              id="signatureData"
              rows={2}
              placeholder="Type your full legal name…"
              disabled={isLoading}
              {...register("signatureData")}
              className={cn(TEXTAREA_CLS, errors.signatureData && "border-destructive")}
            />
            {errors.signatureData && (
              <p className="text-xs text-destructive">{errors.signatureData.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <FileUploadField
              acceptedTypes={PDF_ACCEPTED_TYPES}
              keyPrefix={`consent/${user?.id ?? "anon"}`}
              label="Consent document (optional)"
              disabled={isLoading}
              onUploaded={(url) => setPdfUrl(url)}
            />
            {pdfUrl && (
              <div className="flex items-center gap-2 rounded-md border border-input px-3 py-2 text-sm">
                <FileText className="h-4 w-4 shrink-0 text-muted-foreground" />
                <span className="flex-1 truncate text-xs text-muted-foreground">PDF uploaded</span>
                <button
                  type="button"
                  onClick={() => setPdfUrl(null)}
                  className="text-xs text-destructive hover:underline shrink-0"
                  disabled={isLoading}
                >
                  Remove
                </button>
              </div>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={isLoading}>
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Signing…
              </>
            ) : (
              "Sign Consent Form"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
