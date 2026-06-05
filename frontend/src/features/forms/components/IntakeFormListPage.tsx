import { ClipboardList, Loader2 } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetIntakeFormsQuery } from "../intakeFormsApi";
import type { IntakeFormResponse } from "../form.types";

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function IntakeFormRow({ form }: { form: IntakeFormResponse }) {
  const navigate = useNavigate();

  return (
    <Card
      className="cursor-pointer hover:bg-muted/40 transition-colors"
      onClick={() => navigate(`/forms/intake/${form.id}`)}
    >
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="space-y-0.5 min-w-0">
            <p className="text-sm font-medium truncate">
              {form.formData.length > 80
                ? form.formData.slice(0, 80) + "…"
                : form.formData}
            </p>
            <p className="text-xs text-muted-foreground">
              Client: <span className="font-mono">{form.clientId.slice(0, 8)}…</span>
            </p>
            {form.appointmentId && (
              <p className="text-xs text-muted-foreground">
                Appt: <span className="font-mono">{form.appointmentId.slice(0, 8)}…</span>
              </p>
            )}
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
  const [searchParams] = useSearchParams();
  const clientId      = searchParams.get("clientId")      ?? undefined;
  const appointmentId = searchParams.get("appointmentId") ?? undefined;

  const { data: forms, isLoading, isError } = useGetIntakeFormsQuery({ clientId, appointmentId });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <ClipboardList className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Intake Forms</span>
        </div>
        {forms && (
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <ClipboardList className="h-3.5 w-3.5" />
            <span>
              {forms.length} form{forms.length !== 1 ? "s" : ""}
            </span>
          </div>
        )}
      </header>

      {(clientId || appointmentId) && (
        <div className="px-6 py-2 border-b bg-muted/30 flex flex-wrap gap-2 text-xs text-muted-foreground">
          {clientId      && <span>Client: <span className="font-mono">{clientId}</span></span>}
          {appointmentId && <span>Appointment: <span className="font-mono">{appointmentId}</span></span>}
        </div>
      )}

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading intake forms…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load intake forms. Please try again.
          </p>
        )}

        {!isLoading && !isError && forms?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No intake forms found.
          </p>
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
