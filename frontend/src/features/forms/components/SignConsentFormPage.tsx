import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, CheckCircle, FileSignature, Loader2 } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { cn } from "@/shared/utils/cn";
import { useCurrentUser } from "@/shared/hooks/useCurrentUser";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetMyAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useSignConsentFormMutation, useGetActiveConsentTemplateQuery } from "../consentFormsApi";

const schema = z.object({
  appointmentId: z.string().min(1, "Please select an appointment"),
  signatureData: z.string().min(2, "Please type your full name to sign"),
});

type FormValues = z.infer<typeof schema>;

export function SignConsentFormPage() {
  useDocumentMeta({ title: "Sign Consent Form — TattooOS", canonical: "/forms/consent/new" });

  const navigate = useNavigate();
  const user = useCurrentUser();

  const { data: appointments, isLoading: loadingAppts } = useGetMyAppointmentsQuery();
  const { data: activeTemplate } = useGetActiveConsentTemplateQuery();
  const relevantAppointments = appointments?.filter(
    (a) => a.status === "Pending" || a.status === "Confirmed",
  );
  const [signConsentForm, { isLoading, isSuccess, isError, error, reset: resetMutation }] =
    useSignConsentFormMutation();

  const isDuplicateSignature =
    isError && !!error && "status" in error && error.status === 409;

  const {
    register,
    control,
    handleSubmit,
    reset: resetForm,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { appointmentId: "", signatureData: "" },
  });

  async function onSubmit(values: FormValues) {
    if (!user) return;
    const result = await signConsentForm({
      clientId:      user.id,
      appointmentId: values.appointmentId,
      signatureData: values.signatureData,
    });
    if ("data" in result) {
      toast.success("Consent form signed.");
      resetForm();
    } else {
      const status = "error" in result && "status" in result.error ? result.error.status : undefined;
      toast.error(
        status === 409
          ? "You've already signed a consent form for this appointment."
          : "Failed to sign consent form.",
      );
    }
  }

  if (isSuccess) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="text-center space-y-4 px-6">
          <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
          <p className="text-base font-medium">Consent form signed!</p>
          <p className="text-sm text-muted-foreground">
            Your signature has been recorded. A PDF copy has been generated and attached to your appointment.
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
        {activeTemplate?.bodyText ? (
          <section aria-label="Consent agreement" className="mb-5">
            <h2 className="text-sm font-semibold mb-2">
              Consent agreement
              {activeTemplate.version ? ` (v${activeTemplate.version})` : ""}
            </h2>
            {/* The full, active consent text the client is agreeing to — shown before the
                signature field so consent is specific and informed (GDPR Art. 7). The exact
                text is snapshotted server-side at signing time. */}
            <div className="max-h-64 overflow-y-auto whitespace-pre-wrap rounded-md border bg-muted/20 p-4 text-sm text-foreground/90">
              {activeTemplate.bodyText}
            </div>
          </section>
        ) : null}

        <p className="text-sm text-muted-foreground mb-6">
          By signing this consent form you acknowledge the risks and procedures associated with your
          tattoo session. Type your full legal name below to provide your digital signature. A PDF
          document will be generated and attached to your appointment record automatically.
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="appointmentId">Appointment</Label>
            <Controller
              control={control}
              name="appointmentId"
              render={({ field }) => (
                <Select
                  disabled={loadingAppts || isLoading}
                  value={field.value}
                  onValueChange={field.onChange}
                >
                  <SelectTrigger
                    id="appointmentId"
                    className={cn(errors.appointmentId && "border-destructive")}
                  >
                    <SelectValue
                      placeholder={loadingAppts ? "Loading appointments…" : "Select an appointment"}
                    />
                  </SelectTrigger>
                  <SelectContent>
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
            {errors.appointmentId && (
              <p className="text-xs text-destructive">{errors.appointmentId.message}</p>
            )}
            {!loadingAppts && relevantAppointments?.length === 0 && (
              <p className="text-xs text-muted-foreground">
                No pending appointments found. Book an appointment before signing a consent form.
              </p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="signatureData">Digital signature (full name)</Label>
            <Textarea
              id="signatureData"
              rows={2}
              placeholder="e.g. Jane Marie Smith"
              disabled={isLoading}
              {...register("signatureData")}
              className={cn("resize-none", errors.signatureData && "border-destructive")}
            />
            {errors.signatureData && (
              <p className="text-xs text-destructive">{errors.signatureData.message}</p>
            )}
          </div>

          {isError && (
            <p className="text-sm text-destructive text-center">
              {isDuplicateSignature
                ? "You've already signed a consent form for this appointment."
                : "Failed to sign. Please try again."}
            </p>
          )}

          <Button
            type="submit"
            className="w-full"
            disabled={isLoading || (!loadingAppts && relevantAppointments?.length === 0)}
          >
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
