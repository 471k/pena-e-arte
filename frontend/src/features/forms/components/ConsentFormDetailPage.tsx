import { ArrowLeft, Check, Copy, Download, ExternalLink, FileSignature } from "lucide-react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useCopyToClipboard } from "@/shared/hooks/useCopyToClipboard";
import { formatRelativeTimeFromNow } from "@/shared/utils/formatRelativeTime";
import { useGetConsentFormByIdQuery } from "../consentFormsApi";
import type { ConsentFormDetailResponse } from "../form.types";

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

// ── Sub-components ────────────────────────────────────────────────────────────

function PageHeader({ onBack }: { onBack: () => void }) {
  return (
    <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
      <Button
        variant="ghost"
        size="sm"
        onClick={onBack}
        className="gap-1.5"
        aria-label="Back to Consent Forms"
      >
        <ArrowLeft className="h-4 w-4" />
        Consent Forms
      </Button>
      <div className="flex items-center gap-2">
        <FileSignature className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Consent Form</span>
      </div>
    </header>
  );
}

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      {/* text-foreground/65 ≈ 6.2:1 on #000 dark background — passes WCAG AA */}
      <p className="text-xs font-medium text-foreground/65 uppercase tracking-wider">{label}</p>
      <div className="text-sm text-foreground">{children}</div>
    </div>
  );
}

function SignatureDisplay({ value }: { value: string }) {
  const isImage = value.startsWith("data:image/");

  if (isImage) {
    return (
      <img
        src={value}
        alt="Digital signature"
        className="max-h-20 max-w-xs border-b border-foreground/20 pb-1 object-contain"
      />
    );
  }

  // Text / typed-name signature
  return (
    <p className="font-medium text-base italic border-b border-foreground/20 pb-1 font-serif">
      {value}
    </p>
  );
}

function ConsentFormDetail({ form }: { form: ConsentFormDetailResponse }) {
  const navigate = useNavigate();
  const [copied, copy] = useCopyToClipboard();

  const isPdf = form.fileUrl?.toLowerCase().endsWith(".pdf") ?? false;
  const docLabel = isPdf ? "View signed consent (PDF)" : "View document";

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="p-5 pb-0">
          {/* ── Status row ── */}
          <div className="flex items-center justify-between gap-3">
            <span
              className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                form.signedAt
                  ? "bg-green-500/15 text-green-600 dark:text-green-400"
                  : "bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
              }`}
              aria-label={`Status: ${form.signedAt ? "Signed" : "Pending"}`}
            >
              {form.signedAt ? "Signed" : "Pending"}
            </span>

            {/* Truncated ID with copy */}
            <div className="flex items-center gap-1.5">
              <span className="text-xs text-foreground/65 font-mono" aria-label="Form ID">
                {form.id.slice(0, 8)}…
              </span>
              <button
                type="button"
                onClick={() => copy(form.id)}
                className="text-foreground/65 hover:text-foreground transition-colors"
                aria-label="Copy full form ID"
              >
                {copied
                  ? <Check  className="h-3.5 w-3.5 text-green-500" />
                  : <Copy   className="h-3.5 w-3.5" />}
              </button>
            </div>
          </div>
        </CardHeader>

        <CardContent className="p-5 pt-4 space-y-5">
          {/* ── Identity fields ── */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <DetailRow label="Client">
              <Link
                to={`/clients/${form.clientId}`}
                className="font-medium hover:underline underline-offset-2 text-primary"
              >
                {form.clientName}
              </Link>
            </DetailRow>

            <DetailRow label="Appointment">
              <Link
                to={`/appointments/${form.appointmentId}`}
                className="font-medium hover:underline underline-offset-2 text-primary"
              >
                {new Date(form.appointmentDate).toLocaleDateString("en-GB", {
                  weekday: "short", day: "numeric", month: "short", year: "numeric",
                })}
              </Link>
              {form.artistName && (
                <p className="text-xs text-foreground/65 mt-0.5">
                  {form.artistName}
                </p>
              )}
            </DetailRow>
          </div>

          {/* ── Signature ── */}
          {form.signatureData && (
            <>
              <Separator />
              <DetailRow label="Digital signature">
                <SignatureDisplay value={form.signatureData} />
              </DetailRow>
            </>
          )}

          {/* ── What was agreed (immutable snapshot) ── */}
          {form.consentTextSnapshot && (
            <>
              <Separator />
              <DetailRow label="Consent agreement (as signed)">
                {/* The exact text agreed to at signing time — deliberately the stored
                    snapshot, not a live re-render of the template, so anyone reviewing a
                    past consent sees exactly what was agreed even if the studio's wording
                    has since changed. */}
                <div className="max-h-64 overflow-y-auto whitespace-pre-wrap rounded-md border bg-muted/20 p-3 text-sm text-foreground/90">
                  {form.consentTextSnapshot}
                </div>
              </DetailRow>
            </>
          )}

          {/* ── Document link + download ── */}
          {form.fileUrl && (
            <>
              <Separator />
              <DetailRow label="Consent document">
                <div className="flex items-center gap-3 flex-wrap">
                  <a
                    href={form.fileUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 text-sm text-primary underline underline-offset-2 hover:opacity-80"
                    aria-label={docLabel}
                  >
                    <ExternalLink className="h-3.5 w-3.5" aria-hidden />
                    {docLabel}
                  </a>

                  {isPdf && (
                    <a
                      href={form.fileUrl}
                      download
                      className="inline-flex items-center gap-1.5 text-sm text-foreground/60 hover:text-foreground transition-colors"
                      aria-label="Download signed consent form PDF"
                    >
                      <Download className="h-3.5 w-3.5" aria-hidden />
                      Download
                    </a>
                  )}
                </div>
              </DetailRow>
            </>
          )}

          {/* ── Timestamps ── */}
          <Separator />
          <div className="grid grid-cols-2 gap-4">
            <DetailRow label="Created">
              <span>{formatDateTime(form.createdAt)}</span>
              <p className="text-xs text-foreground/65 mt-0.5">
                {formatRelativeTimeFromNow(form.createdAt)}
              </p>
            </DetailRow>

            {form.signedAt && (
              <DetailRow label="Signed">
                <span>{formatDateTime(form.signedAt)}</span>
                <p className="text-xs text-foreground/65 mt-0.5">
                  {formatRelativeTimeFromNow(form.signedAt)}
                </p>
              </DetailRow>
            )}
          </div>
        </CardContent>
      </Card>

      {/* ── Back to appointment CTA ── */}
      <div className="flex justify-end">
        <Button
          variant="outline"
          size="sm"
          onClick={() => navigate(`/appointments/${form.appointmentId}`)}
          className="gap-1.5"
        >
          <ArrowLeft className="h-3.5 w-3.5" />
          Back to appointment
        </Button>
      </div>
    </div>
  );
}

// ── Page shell ────────────────────────────────────────────────────────────────

export function ConsentFormDetailPage() {
  const { id }    = useParams<{ id: string }>();
  const navigate  = useNavigate();

  useDocumentMeta({
    title:     "Consent Form — TattooOS",
    canonical: id ? `/forms/consent/${id}` : "/forms/consent",
  });

  const { data: form, isLoading, isError, error } =
    useGetConsentFormByIdQuery(id ?? "", { skip: !id });

  // Distinguish 404 from other errors (RTK Query exposes status on the error object)
  const isNotFound =
    isError &&
    !!error &&
    "status" in error &&
    error.status === 404;

  const goBack = () => navigate("/forms/consent");

  return (
    <div className="min-h-screen bg-background">
      <PageHeader onBack={goBack} />

      <main className="max-w-2xl mx-auto px-4 py-6">
        {/* ── Loading skeleton ── */}
        {isLoading && (
          <Card aria-label="Loading consent form">
            <CardContent className="p-5 space-y-5">
              <div className="flex items-center justify-between">
                <Skeleton className="h-5 w-16 rounded-full" />
                <Skeleton className="h-4 w-24" />
              </div>
              {Array.from({ length: 4 }).map((_, i) => (
                <div key={i} className="space-y-1.5">
                  <Skeleton className="h-3 w-20" />
                  <Skeleton className="h-5 w-full" />
                </div>
              ))}
            </CardContent>
          </Card>
        )}

        {/* ── Not found ── */}
        {isNotFound && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <FileSignature className="h-10 w-10 text-muted-foreground/40" />
            <div className="space-y-1">
              <p className="text-sm font-medium">Consent form not found</p>
              <p className="text-xs text-muted-foreground">
                This form may have been removed, or you may not have permission to view it.
              </p>
            </div>
            <Button variant="outline" size="sm" onClick={goBack}>
              Back to Consent Forms
            </Button>
          </div>
        )}

        {/* ── Generic error ── */}
        {isError && !isNotFound && (
          <p className="text-center text-sm text-destructive-text py-16" role="alert">
            Failed to load consent form. Please try again.
          </p>
        )}

        {/* ── Data ── */}
        {form && <ConsentFormDetail form={form} />}
      </main>
    </div>
  );
}
