import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  useGetMyFeedbackReportsQuery,
  SupportRequestForm,
  SupportTicketThread,
} from "@/features/feedback";

const OPEN_STATUSES = new Set(["Open", "Reviewing"]);

export function ContactSupportPanel() {
  const { data: reports, isLoading, isError, refetch } = useGetMyFeedbackReportsQuery({ type: "SupportRequest" });
  const openTicket = reports?.find((r) => OPEN_STATUSES.has(r.status));

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
      </div>
    );
  }

  // Don't fall through to the submission form on a failed lookup — the user may already
  // have an open ticket and submitting again would create a duplicate.
  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center gap-3 py-12 text-center">
        <p className="text-sm text-destructive-text">Failed to check for an existing support ticket.</p>
        <Button size="sm" variant="outline" onClick={() => refetch()}>Retry</Button>
      </div>
    );
  }

  if (openTicket) {
    return <SupportTicketThread report={openTicket} />;
  }

  return <SupportRequestForm />;
}
