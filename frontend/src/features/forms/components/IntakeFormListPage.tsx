import { ClipboardList, Plus, User } from "lucide-react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetIntakeFormsQuery } from "../intakeFormsApi";
import type { IntakeFormResponse } from "../form.types";

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function formatDateShort(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function parseFormData(raw: string): { name: string | null; dob: string | null } {
  try {
    const obj: unknown = JSON.parse(raw);
    if (typeof obj === "object" && obj !== null && !Array.isArray(obj)) {
      const data = obj as Record<string, unknown>;
      const name = typeof data.fullName === "string" && data.fullName ? data.fullName : null;
      const dob  = typeof data.dateOfBirth === "string" && data.dateOfBirth ? data.dateOfBirth : null;
      return { name, dob };
    }
  } catch { /* plain text */ }
  return { name: null, dob: null };
}

function IntakeFormRow({ form }: { form: IntakeFormResponse }) {
  const navigate = useNavigate();
  const { name, dob } = parseFormData(form.formData);

  const headline = name ?? (
    form.formData.length > 60 ? form.formData.slice(0, 60) + "…" : form.formData
  );

  return (
    <Card
      className="cursor-pointer hover:bg-muted/40 transition-colors"
      onClick={() => navigate(`/forms/intake/${form.id}`)}
    >
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-start gap-3 min-w-0">
            <div className="mt-0.5 shrink-0 rounded-full bg-muted p-1.5">
              <User className="h-3.5 w-3.5 text-muted-foreground" />
            </div>
            <div className="space-y-0.5 min-w-0">
              <p className="text-sm font-medium truncate">{headline}</p>
              {dob && (
                <p className="text-xs text-muted-foreground">
                  DOB: {formatDateShort(dob)}
                </p>
              )}
              {form.appointmentId && (
                <p className="text-xs text-muted-foreground">
                  Appt: <span className="font-mono">{form.appointmentId.slice(0, 8)}…</span>
                </p>
              )}
            </div>
          </div>
          <div className="shrink-0 text-right space-y-0.5">
            <p className="text-xs text-muted-foreground">
              {form.submittedAt ? formatDate(form.submittedAt) : formatDate(form.createdAt)}
            </p>
            <span
              className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                form.submittedAt
                  ? "bg-green-500/15 text-green-600 dark:text-green-400"
                  : "bg-muted text-muted-foreground"
              }`}
            >
              {form.submittedAt ? "Submitted" : "Draft"}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export function IntakeFormListPage() {
  useDocumentMeta({ title: "Intake Forms — TattooOS", canonical: "/forms/intake" });

  const navigate = useNavigate();
  const role = useAppSelector((s) => s.auth.role);
  const isClient = role === Role.Client;
  const [searchParams] = useSearchParams();
  const clientId      = searchParams.get("clientId")      ?? undefined;
  const appointmentId = searchParams.get("appointmentId") ?? undefined;

  const { data: forms, isLoading, isError, refetch } = useGetIntakeFormsQuery({ clientId, appointmentId });
  const errorMessage = useSuspensionAwareError(isError, "Failed to load intake forms.");

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <ClipboardList className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Intake Forms</span>
        </div>
        <div className="flex items-center gap-3">
          {forms && forms.length > 0 && (
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <ClipboardList className="h-3.5 w-3.5" />
              <span>
                {forms.length} form{forms.length !== 1 ? "s" : ""}
              </span>
            </div>
          )}
          {isClient && (
            <Button size="sm" onClick={() => navigate("/forms/intake/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              Submit intake form
            </Button>
          )}
        </div>
      </header>

      {(clientId || appointmentId) && (
        <div className="px-6 py-2 border-b bg-muted/30 flex flex-wrap gap-2 text-xs text-muted-foreground">
          {clientId      && <span>Client: <span className="font-mono">{clientId}</span></span>}
          {appointmentId && <span>Appointment: <span className="font-mono">{appointmentId}</span></span>}
        </div>
      )}

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-16 w-full rounded-lg" />
            ))}
          </div>
        )}

        {errorMessage && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            {errorMessage}{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {!isLoading && !isError && forms?.length === 0 && (clientId || appointmentId) && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No intake forms found.
          </p>
        )}

        {!isLoading && !isError && forms?.length === 0 && !clientId && !appointmentId && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <ClipboardList className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No intake forms yet</p>
              <p className="text-xs text-muted-foreground">
                {isClient
                  ? "You haven't submitted any intake forms yet."
                  : "Intake forms appear here after clients submit them during booking."}
              </p>
            </div>
            {isClient && (
              <Button size="sm" onClick={() => navigate("/forms/intake/new")} className="gap-1.5">
                <Plus className="h-3.5 w-3.5" />
                Submit intake form
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && forms && forms.length > 0 && (
          <div className="space-y-2">
            {forms.map((form) => (
              <IntakeFormRow key={form.id} form={form} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
