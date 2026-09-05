import { ArrowLeft, Check, ClipboardList, Minus, Paperclip } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { useGetIntakeFormByIdQuery } from "../intakeFormsApi";

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "long", year: "numeric",
  });
}

// ── structured medical history ──────────────────────────────────────────────

interface MedicalHistoryData {
  fullName?:             string;
  dateOfBirth?:          string;
  hasBloodCondition?:    boolean;
  hasDiabetes?:          boolean;
  takesBloodThinners?:   boolean;
  hasAllergies?:         boolean;
  allergyDetails?:       string;
  hasSkinCondition?:     boolean;
  isPregnant?:           boolean;
  acknowledgesAftercare?: boolean;
  [key: string]:         unknown;
}

function BoolChip({ value }: { value: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium",
        value
          ? "bg-green-500/15 text-green-600 dark:text-green-400"
          : "bg-muted text-muted-foreground"
      )}
    >
      {value ? <Check className="h-3 w-3" /> : <Minus className="h-3 w-3" />}
      {value ? "Yes" : "No"}
    </span>
  );
}

function HealthFlag({ label, value }: { label: string; value: boolean }) {
  return (
    <div className="flex items-center justify-between gap-2">
      <span className="text-sm text-muted-foreground">{label}</span>
      <BoolChip value={value} />
    </div>
  );
}

function MedicalHistoryView({ raw }: { raw: string }) {
  let parsed: MedicalHistoryData | null = null;
  try {
    const obj: unknown = JSON.parse(raw);
    if (typeof obj === "object" && obj !== null && !Array.isArray(obj)) {
      parsed = obj as MedicalHistoryData;
    }
  } catch {
    // not JSON — fall through to plain text
  }

  if (!parsed) {
    return <p className="whitespace-pre-wrap leading-relaxed text-sm">{raw}</p>;
  }

  const hasHealthFlags =
    parsed.hasBloodCondition !== undefined ||
    parsed.hasDiabetes       !== undefined ||
    parsed.takesBloodThinners !== undefined ||
    parsed.hasAllergies      !== undefined ||
    parsed.hasSkinCondition  !== undefined ||
    parsed.isPregnant        !== undefined;

  return (
    <div className="space-y-4">
      {/* Personal info */}
      {(parsed.fullName || parsed.dateOfBirth) && (
        <div className="space-y-2">
          {parsed.fullName && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Full name</span>
              <span className="text-sm font-medium">{parsed.fullName}</span>
            </div>
          )}
          {parsed.dateOfBirth && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Date of birth</span>
              <span className="text-sm">{formatDate(parsed.dateOfBirth)}</span>
            </div>
          )}
        </div>
      )}

      {/* Health flags */}
      {hasHealthFlags && (
        <>
          {(parsed.fullName || parsed.dateOfBirth) && <Separator />}
          <div className="space-y-2">
            <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
              Health conditions
            </p>
            <div className="space-y-1.5">
              {parsed.hasBloodCondition  !== undefined && <HealthFlag label="Blood condition"    value={parsed.hasBloodCondition} />}
              {parsed.hasDiabetes        !== undefined && <HealthFlag label="Diabetes"           value={parsed.hasDiabetes} />}
              {parsed.takesBloodThinners !== undefined && <HealthFlag label="Takes blood thinners" value={parsed.takesBloodThinners} />}
              {parsed.hasAllergies       !== undefined && <HealthFlag label="Allergies"          value={parsed.hasAllergies} />}
              {parsed.hasSkinCondition   !== undefined && <HealthFlag label="Skin condition"     value={parsed.hasSkinCondition} />}
              {parsed.isPregnant         !== undefined && <HealthFlag label="Pregnant"           value={parsed.isPregnant} />}
            </div>
          </div>
        </>
      )}

      {/* Allergy detail text */}
      {parsed.allergyDetails && (
        <>
          <Separator />
          <div className="space-y-0.5">
            <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">Allergy details</p>
            <p className="text-sm">{parsed.allergyDetails}</p>
          </div>
        </>
      )}

      {/* Aftercare acknowledgment */}
      {parsed.acknowledgesAftercare !== undefined && (
        <>
          <Separator />
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Acknowledges aftercare instructions</span>
            <BoolChip value={parsed.acknowledgesAftercare} />
          </div>
        </>
      )}
    </div>
  );
}

// ── page ────────────────────────────────────────────────────────────────────

function DetailRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">{label}</p>
      <div className="text-sm">{value}</div>
    </div>
  );
}

export function IntakeFormDetailPage() {
  const { id }   = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: form, isLoading, isError } =
    useGetIntakeFormByIdQuery(id ?? "", { skip: !id });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/forms/intake")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Intake Forms
        </Button>
        <div className="flex items-center gap-2">
          <ClipboardList className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Intake Form Detail</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6">
        {isLoading && (
          <div className="space-y-4" aria-label="Loading intake form">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="space-y-1.5">
                <Skeleton className="h-3 w-20" />
                <Skeleton className="h-5 w-full" />
              </div>
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load intake form. Please try again.
          </p>
        )}

        {form && (
          <Card>
            <CardContent className="p-5 space-y-5">
              {/* Status row */}
              <div className="flex items-center justify-between">
                <span
                  className={cn(
                    "inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium",
                    form.submittedAt
                      ? "bg-green-500/15 text-green-600 dark:text-green-400"
                      : "bg-muted text-muted-foreground"
                  )}
                >
                  {form.submittedAt && <Check className="h-3 w-3" />}
                  {form.submittedAt ? "Submitted" : "Draft"}
                </span>
                <span className="text-xs text-muted-foreground font-mono">
                  {form.id.slice(0, 8)}…
                </span>
              </div>

              {/* Medical history — structured or plain text */}
              <div className="space-y-1.5">
                <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                  Medical history &amp; notes
                </p>
                <MedicalHistoryView raw={form.formData} />
              </div>

              <Separator />

              {/* Metadata */}
              <div className="grid grid-cols-2 gap-4">
                <DetailRow
                  label="Created"
                  value={<span className="text-xs">{formatDateTime(form.createdAt)}</span>}
                />
                {form.submittedAt && (
                  <DetailRow
                    label="Submitted"
                    value={<span className="text-xs">{formatDateTime(form.submittedAt)}</span>}
                  />
                )}
              </div>

              {form.fileUrl && (
                <DetailRow
                  label="Attachment"
                  value={
                    <a
                      href={form.fileUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 text-sm text-primary underline underline-offset-2 hover:opacity-80"
                    >
                      <Paperclip className="h-3.5 w-3.5" />
                      View file
                    </a>
                  }
                />
              )}
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}
