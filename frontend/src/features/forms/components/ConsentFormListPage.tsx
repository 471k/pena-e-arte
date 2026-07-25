import { FileSignature, Plus } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetConsentFormsQuery } from "../consentFormsApi";
import type { ConsentFormResponse } from "../form.types";

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function ConsentFormRow({ form }: { form: ConsentFormResponse }) {
  const navigate = useNavigate();

  return (
    <Card
      className="cursor-pointer hover:bg-muted/40 transition-colors"
      onClick={() => navigate(`/forms/consent/${form.id}`)}
    >
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="space-y-0.5 min-w-0">
            <p className="text-sm font-medium">
              Consent Form
            </p>
            <p className="text-xs text-muted-foreground">
              {form.clientName || form.clientId.slice(0, 8) + "…"}
            </p>
            <p className="text-xs text-muted-foreground font-mono">
              {form.appointmentId.slice(0, 8)}…
            </p>
          </div>
          <div className="shrink-0 text-right space-y-0.5">
            <p className="text-xs text-muted-foreground">
              {form.signedAt ? formatDate(form.signedAt) : formatDate(form.createdAt)}
            </p>
            <span
              className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                form.signedAt
                  ? "bg-green-500/15 text-green-600 dark:text-green-400"
                  : "bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
              }`}
            >
              {form.signedAt ? "Signed" : "Pending"}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export function ConsentFormListPage() {
  useDocumentMeta({ title: "Consent Forms — TattooOS", canonical: "/forms/consent" });

  const navigate = useNavigate();
  const role = useAppSelector((s) => s.auth.role);
  const isClient = role === Role.Client;
  const [searchParams] = useSearchParams();
  const clientId      = searchParams.get("clientId")      ?? undefined;
  const appointmentId = searchParams.get("appointmentId") ?? undefined;

  const { data: forms, isLoading, isError, refetch } = useGetConsentFormsQuery({ clientId, appointmentId });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <FileSignature className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Consent Forms</span>
        </div>
        <div className="flex items-center gap-3">
          {forms && forms.length > 0 && (
            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <FileSignature className="h-3.5 w-3.5" />
              <span>
                {forms.length} form{forms.length !== 1 ? "s" : ""}
              </span>
            </div>
          )}
          {isClient && (
            <Button size="sm" onClick={() => navigate("/forms/consent/new")} className="gap-1.5">
              <Plus className="h-3.5 w-3.5" />
              Sign consent form
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

        {isError && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            Failed to load consent forms.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {!isLoading && !isError && forms?.length === 0 && (clientId || appointmentId) && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No consent forms found.
          </p>
        )}

        {!isLoading && !isError && forms?.length === 0 && !clientId && !appointmentId && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <FileSignature className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium text-foreground">No signed consent forms yet</p>
              <p className="text-xs text-muted-foreground">
                {isClient
                  ? "You haven't signed any consent forms yet."
                  : "Consent forms appear here after clients sign them during booking."}
              </p>
            </div>
            {isClient && (
              <Button size="sm" onClick={() => navigate("/forms/consent/new")} className="gap-1.5">
                <Plus className="h-3.5 w-3.5" />
                Sign consent form
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && forms && forms.length > 0 && (
          <div className="space-y-2">
            {forms.map((form) => (
              <ConsentFormRow key={form.id} form={form} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
