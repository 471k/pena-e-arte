import { ArrowLeft, FileSignature, Paperclip } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetConsentFormByIdQuery } from "../consentFormsApi";

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function DetailRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">{label}</p>
      <div className="text-sm">{value}</div>
    </div>
  );
}

export function ConsentFormDetailPage() {
  const { id }   = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: form, isLoading, isError } =
    useGetConsentFormByIdQuery(id ?? "", { skip: !id });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/forms/consent")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Consent Forms
        </Button>
        <div className="flex items-center gap-2">
          <FileSignature className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Consent Form Detail</span>
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6">
        {isLoading && (
          <div className="space-y-4" aria-label="Loading consent form">
            {Array.from({ length: 5 }).map((_, i) => (
              <div key={i} className="space-y-1.5">
                <Skeleton className="h-3 w-20" />
                <Skeleton className="h-5 w-full" />
              </div>
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load consent form. Please try again.
          </p>
        )}

        {form && (
          <Card>
            <CardContent className="p-5 space-y-5">
              <div className="flex items-center justify-between">
                <span
                  className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                    form.signedAt
                      ? "bg-green-500/15 text-green-600 dark:text-green-400"
                      : "bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
                  }`}
                >
                  {form.signedAt ? "Signed" : "Pending"}
                </span>
                <span className="text-xs text-muted-foreground font-mono">
                  {form.id.slice(0, 8)}…
                </span>
              </div>

              <div className="grid grid-cols-1 gap-4">
                <DetailRow
                  label="Client ID"
                  value={<span className="font-mono text-xs">{form.clientId}</span>}
                />
                <DetailRow
                  label="Appointment ID"
                  value={<span className="font-mono text-xs">{form.appointmentId}</span>}
                />
              </div>

              {form.signatureData && (
                <DetailRow
                  label="Digital signature"
                  value={
                    <p className="font-medium text-base italic border-b border-foreground/20 pb-1">
                      {form.signatureData}
                    </p>
                  }
                />
              )}

              {form.fileUrl && (
                <DetailRow
                  label="Consent document"
                  value={
                    <a
                      href={form.fileUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 text-sm text-primary underline underline-offset-2 hover:opacity-80"
                    >
                      <Paperclip className="h-3.5 w-3.5" />
                      View document
                    </a>
                  }
                />
              )}

              <div className="grid grid-cols-2 gap-4 pt-2 border-t">
                <DetailRow
                  label="Created"
                  value={<span className="text-xs">{formatDateTime(form.createdAt)}</span>}
                />
                {form.signedAt && (
                  <DetailRow
                    label="Signed"
                    value={<span className="text-xs">{formatDateTime(form.signedAt)}</span>}
                  />
                )}
              </div>
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}
