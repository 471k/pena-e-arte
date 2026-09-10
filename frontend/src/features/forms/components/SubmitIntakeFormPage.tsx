import { useMemo, useState } from "react";
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
import { useGetActiveConsentTemplateQuery } from "@/features/forms/consentFormsApi";
import { useSubmitIntakeFormMutation, useGetActiveIntakeFormTemplateQuery } from "../intakeFormsApi";
import type { IntakeFormFieldDefinition } from "../form.types";

const DEFAULT_CONSENT_TEXT =
  "By submitting this form, you consent to sharing your medical history and " +
  "other health-related information you provide here with the studio, for the " +
  "purpose of preparing for and conducting your tattoo session.";

// formData's own min-length rule is enforced imperatively in onSubmit instead of here — it only
// applies on the no-template fallback path (see MIN_FORM_DATA_LENGTH below), while the
// template path validates its own per-field `required` flags instead.
const schema = z.object({
  formData:        z.string().optional(),
  appointmentId:   z.string().optional(),
  fileUrl:         z.string().url("Must be a valid URL").optional().or(z.literal("")),
  consentAccepted: z.boolean().refine((v) => v === true, "You must consent before submitting"),
});

type FormValues = z.infer<typeof schema>;

const MIN_FORM_DATA_LENGTH = 10;

type DynamicFieldValue = string | boolean;

function defaultValueFor(field: IntakeFormFieldDefinition): DynamicFieldValue {
  return field.type === "Checkbox" ? false : "";
}

function DynamicIntakeField({
  field,
  value,
  onChange,
  error,
  disabled,
  index,
}: {
  field:    IntakeFormFieldDefinition;
  value:    DynamicFieldValue;
  onChange: (v: DynamicFieldValue) => void;
  error?:   string;
  disabled: boolean;
  index:    number;
}) {
  const id = `dynamic-field-${index}`;

  return (
    <div className="space-y-1.5">
      {field.type !== "Checkbox" && (
        <Label htmlFor={id}>
          {field.label}{field.required && <span aria-hidden="true"> *</span>}
        </Label>
      )}

      {field.type === "Text" && (
        <Input
          id={id}
          value={value as string}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className={cn(error && "border-destructive")}
        />
      )}
      {field.type === "Textarea" && (
        <Textarea
          id={id}
          rows={4}
          value={value as string}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className={cn("resize-none", error && "border-destructive")}
        />
      )}
      {field.type === "Date" && (
        <Input
          id={id}
          type="date"
          value={value as string}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className={cn(error && "border-destructive")}
        />
      )}
      {field.type === "Select" && (
        <Select value={(value as string) || undefined} onValueChange={onChange} disabled={disabled}>
          <SelectTrigger id={id} className={cn(error && "border-destructive")}>
            <SelectValue placeholder="Select…" />
          </SelectTrigger>
          <SelectContent>
            {(field.options ?? []).map((opt) => (
              <SelectItem key={opt} value={opt}>{opt}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
      {field.type === "Checkbox" && (
        <label htmlFor={id} className="flex items-center gap-2 cursor-pointer select-none text-sm">
          <input
            id={id}
            type="checkbox"
            checked={value as boolean}
            disabled={disabled}
            onChange={(e) => onChange(e.target.checked)}
            className="h-4 w-4 rounded border-input accent-primary"
          />
          {field.label}{field.required && <span aria-hidden="true"> *</span>}
        </label>
      )}

      {error && <p className="text-xs text-destructive-text">{error}</p>}
    </div>
  );
}

export function SubmitIntakeFormPage() {
  useDocumentMeta({ title: "Submit Intake Form — TattooOS", canonical: "/forms/intake/new" });

  const navigate = useNavigate();
  const user = useCurrentUser();

  const { data: appointments, isLoading: loadingAppts } = useGetMyAppointmentsQuery();
  const relevantAppointments = appointments?.filter(
    (a) => a.status === "Pending" || a.status === "Confirmed",
  );
  const { data: activeTemplate } = useGetActiveConsentTemplateQuery({ kind: "IntakeFormConsent" });
  const { data: intakeTemplate } = useGetActiveIntakeFormTemplateQuery();
  const [submitIntakeForm, { isLoading, isSuccess, isError, reset: resetMutation }] =
    useSubmitIntakeFormMutation();

  const fields: IntakeFormFieldDefinition[] = useMemo(() => {
    if (!intakeTemplate) return [];
    try {
      const parsed = JSON.parse(intakeTemplate.fieldSchemaJson) as IntakeFormFieldDefinition[];
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }, [intakeTemplate]);

  // When a studio has an active, correctly-configured template, this drives the dynamic
  // fields; otherwise the page renders exactly today's single-textarea fallback.
  const hasTemplate = fields.length > 0;

  const [dynamicValues, setDynamicValues] = useState<Record<string, DynamicFieldValue>>({});
  const [dynamicErrors, setDynamicErrors] = useState<Record<string, string>>({});
  const [formDataError, setFormDataError] = useState<string | null>(null);

  function dynamicValueFor(field: IntakeFormFieldDefinition): DynamicFieldValue {
    return dynamicValues[field.label] ?? defaultValueFor(field);
  }

  const {
    register,
    control,
    handleSubmit,
    reset: resetForm,
    formState: { errors },
  } = useForm<FormValues>({
    resolver:      zodResolver(schema),
    defaultValues: { consentAccepted: false },
  });

  async function onSubmit(values: FormValues) {
    if (!user) return;

    let formData: string;

    if (hasTemplate) {
      const nextErrors: Record<string, string> = {};
      for (const field of fields) {
        const v = dynamicValueFor(field);
        const empty = field.type === "Checkbox" ? v !== true : !String(v).trim();
        if (field.required && empty) nextErrors[field.label] = "This field is required.";
      }
      setDynamicErrors(nextErrors);
      if (Object.keys(nextErrors).length > 0) return;

      formData = JSON.stringify(
        Object.fromEntries(fields.map((f) => [f.label, dynamicValueFor(f)])),
      );
    } else {
      const trimmed = (values.formData ?? "").trim();
      if (trimmed.length < MIN_FORM_DATA_LENGTH) {
        setFormDataError(`Please provide at least ${MIN_FORM_DATA_LENGTH} characters`);
        return;
      }
      setFormDataError(null);
      formData = values.formData ?? "";
    }

    const result = await submitIntakeForm({
      clientId:        user.id,
      formData,
      appointmentId:   values.appointmentId || null,
      fileUrl:         values.fileUrl || null,
      consentAccepted: values.consentAccepted,
    });
    if ("data" in result) {
      resetForm();
      setDynamicValues({});
      setDynamicErrors({});
      setFormDataError(null);
    }
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
          {hasTemplate
            ? "Please fill out the details below your studio needs before your session — your studio's intake form may look different if your studio has customized it."
            : "Please share your medical history and any details your artist should know before your session."}
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          {hasTemplate ? (
            fields.map((field, index) => (
              <DynamicIntakeField
                key={`${field.label}-${index}`}
                field={field}
                value={dynamicValueFor(field)}
                onChange={(v) => setDynamicValues((prev) => ({ ...prev, [field.label]: v }))}
                error={dynamicErrors[field.label]}
                disabled={isLoading}
                index={index}
              />
            ))
          ) : (
            <div className="space-y-1.5">
              <Label htmlFor="formData">Medical history &amp; notes</Label>
              <Textarea
                id="formData"
                rows={6}
                placeholder="List any allergies, skin conditions, medications, or other relevant health information…"
                disabled={isLoading}
                {...register("formData")}
                className={cn("resize-none", formDataError && "border-destructive")}
              />
              {formDataError && (
                <p className="text-xs text-destructive-text">{formDataError}</p>
              )}
            </div>
          )}

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
              <p className="text-xs text-destructive-text">{errors.fileUrl.message}</p>
            )}
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">Consent</p>
            <div className="max-h-40 overflow-y-auto whitespace-pre-wrap rounded-md border bg-muted/20 p-3 text-xs text-foreground/90">
              {activeTemplate?.bodyText || DEFAULT_CONSENT_TEXT}
            </div>
            <label htmlFor="consentAccepted" className="flex items-start gap-2 cursor-pointer select-none">
              <input
                id="consentAccepted"
                type="checkbox"
                disabled={isLoading}
                {...register("consentAccepted")}
                className="mt-0.5 h-4 w-4 rounded border-input accent-primary"
              />
              <span className="text-sm text-muted-foreground">
                I consent to sharing this medical/health information with the studio.
              </span>
            </label>
            {errors.consentAccepted && (
              <p className="text-xs text-destructive-text">{errors.consentAccepted.message}</p>
            )}
          </div>

          {isError && (
            <p className="text-sm text-destructive-text text-center">
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
