import { useId, useState } from "react";
import type { ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import { CheckCircle2, ChevronLeft, CircleDashed, Loader2, RotateCw, Send } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Accordion, AccordionContent, AccordionItem, AccordionTrigger,
} from "@/shared/components/ui/accordion";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { useCreateConversationMutation, useSendMessageMutation } from "@/features/messaging/messagingApi";
import { useGetMyAppointmentQuery } from "../appointmentsApi";
import { AppointmentStatus, ReadinessKey } from "../appointment.types";
import type {
  AppointmentReadinessItem,
  AppointmentResponse,
  ClientAppointmentAftercare,
  ClientAppointmentContact,
  ClientAppointmentDetailResponse,
  ClientAppointmentVisit,
} from "../appointment.types";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatusBadge } from "./DepositStatusBadge";
import { AppointmentImageGallery } from "./AppointmentImageGallery";
import { splitAppointmentImages } from "../appointmentImages";
import { CancelArea, DepositArea, RescheduleArea } from "./MyBookingsSection";

// The client's own appointment page (DEC02), reached from each card in My bookings and deep-linkable
// at /my-appointments/:id. Reading order is fixed by the After Hours handoff:
//   summary → next required action → remaining preparation (completed collapsed) →
//   design / attachments → visit → contact → change / cancel.
// Everything that decides something comes from the server: readiness (DEC03) from
// GET /appointments/mine/{id}, the notice window from `selfService`, and the money consequence of a
// cancellation from the cancellation preview inside CancelArea. There is no policy arithmetic here.

const UPCOMING = new Set<AppointmentStatus>([AppointmentStatus.Pending, AppointmentStatus.Confirmed]);

/** A description longer than this collapses behind "Show more". */
export const DESCRIPTION_PREVIEW_CHARS = 280;

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status: unknown }).status;
    return typeof status === "number" ? status : undefined;
  }
  return undefined;
}

// ── Shell, loading and failure states ────────────────────────────────────

function PageShell({ children }: { children: ReactNode }) {
  return (
    <div className="bg-background flex items-start justify-center px-4 py-12">
      <div className="w-full max-w-md space-y-6">
        <Link
          to="/book"
          className="inline-flex items-center gap-1 text-xs text-muted-foreground
                     hover:text-foreground transition-colors"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          My bookings
        </Link>
        {children}
      </div>
    </div>
  );
}

function PageSkeleton() {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="Loading appointment">
      <div className="rounded-xl border bg-card p-4 space-y-3">
        <Skeleton className="h-6 w-48" />
        <Skeleton className="h-4 w-56" />
        <Skeleton className="h-4 w-40" />
        <div className="flex gap-2 pt-1">
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-5 w-20 rounded-full" />
        </div>
      </div>
      <Skeleton className="h-9 w-full rounded-md" />
      <div className="rounded-xl border bg-card p-4 space-y-2">
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-3/4" />
      </div>
    </div>
  );
}

function NotFoundState() {
  return (
    <Card>
      <CardContent className="p-5 space-y-3">
        <h1 className="text-base font-semibold">Appointment not found</h1>
        <p className="text-sm text-muted-foreground">
          We couldn&apos;t find this appointment in your bookings. It may belong to a different studio
          or account.
        </p>
        <Button asChild variant="outline" size="sm">
          <Link to="/book">Go to my bookings</Link>
        </Button>
      </CardContent>
    </Card>
  );
}

function LoadFailedState({ onRetry, retrying }: { onRetry: () => void; retrying: boolean }) {
  return (
    <Card>
      <CardContent className="p-5 space-y-3" role="alert">
        <h1 className="text-base font-semibold">Couldn&apos;t load this appointment</h1>
        <p className="text-sm text-destructive-text">
          Something went wrong while loading it. Check your connection and try again.
        </p>
        <Button variant="outline" size="sm" onClick={onRetry} disabled={retrying} className="gap-1.5">
          {retrying ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RotateCw className="h-3.5 w-3.5" />}
          Try again
        </Button>
      </CardContent>
    </Card>
  );
}

// ── Sections ─────────────────────────────────────────────────────────────

function Section({ title, children, labelledBy }: { title: string; children: ReactNode; labelledBy: string }) {
  return (
    <section aria-labelledby={labelledBy}>
      <Card>
        <CardContent className="p-4 space-y-3">
          <h2 id={labelledBy} className="text-sm font-semibold">{title}</h2>
          {children}
        </CardContent>
      </Card>
    </section>
  );
}

function Summary({
  appt, depositIsNextAction, timezone,
}: {
  appt: AppointmentResponse;
  depositIsNextAction: boolean;
  timezone: string | undefined;
}) {
  const day = new Date(appt.date).toLocaleDateString("en-GB", withStudioTimeZone({
    weekday: "long", day: "numeric", month: "long", year: "numeric",
  }, timezone));
  const time = (iso: string) => new Date(iso).toLocaleTimeString("en-GB", withStudioTimeZone({
    hour: "2-digit", minute: "2-digit",
  }, timezone));

  const artist = appt.artistId === null
    ? "The studio will assign an artist"
    : appt.artistName ?? "Artist details unavailable";

  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <div className="space-y-1">
          <h1 className="text-lg font-semibold tracking-tight">{appt.serviceName ?? "Tattoo appointment"}</h1>
          <p className="text-sm">{day}</p>
          <p className="text-sm">
            {time(appt.date)} – {time(appt.endDate)}
            <span className="text-muted-foreground"> · {appt.durationMinutes} min</span>
          </p>
          <p className="text-sm text-muted-foreground">{artist}</p>
        </div>
        <dl className="grid grid-cols-[auto_1fr] items-center gap-x-3 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Booking</dt>
          <dd><AppointmentStatusBadge status={appt.status} /></dd>
          <dt className="text-muted-foreground">Deposit</dt>
          <dd>
            {appt.depositAmount <= 0
              ? <span>No deposit</span>
              : depositIsNextAction
                ? <DepositStatusBadge status={appt.depositStatus} />
                : <DepositArea appt={appt} />}
          </dd>
        </dl>
        <StatusNote appt={appt} />
      </CardContent>
    </Card>
  );
}

function StatusNote({ appt }: { appt: AppointmentResponse }) {
  switch (appt.status) {
    case AppointmentStatus.Pending:
      return (
        <p className="text-xs text-muted-foreground">
          This is a request — the studio still has to confirm it.
        </p>
      );
    case AppointmentStatus.Cancelled:
      return (
        <p className="text-xs text-muted-foreground">
          {appt.cancellationReason === "StudioDeclined"
            ? "The studio couldn't take this request."
            : "This appointment was cancelled."}{" "}
          <Link to="/book" className="underline hover:text-foreground">Book a new appointment</Link>
        </p>
      );
    case AppointmentStatus.NoShow:
      return <p className="text-xs text-muted-foreground">The studio marked this appointment as missed.</p>;
    default:
      return null;
  }
}

/** Exactly one filled CTA, for the highest-priority outstanding requirement the client can act on. */
function NextAction({ appt, item }: { appt: AppointmentResponse; item: AppointmentReadinessItem }) {
  return (
    <section aria-labelledby="next-action-heading" className="space-y-2" data-tour="client-appointment-next-action">
      <h2 id="next-action-heading" className="text-sm font-semibold">Next step</h2>
      {item.key === ReadinessKey.DepositPaid ? (
        <DepositArea appt={appt} prominent />
      ) : item.destination ? (
        <Button asChild className="w-full">
          <Link to={item.destination}>{item.actionLabel}</Link>
        </Button>
      ) : null}
    </section>
  );
}

function Preparation({
  items, next,
}: {
  items: AppointmentReadinessItem[];
  next: AppointmentReadinessItem | undefined;
}) {
  // The next action is shown once, above; it is not repeated here.
  const outstanding = items.filter((i) => !i.satisfied && i.key !== next?.key);
  const done = items.filter((i) => i.satisfied);

  return (
    <Section title="Before your appointment" labelledBy="preparation-heading">
      {outstanding.length === 0 && !next && (
        <p className="flex items-center gap-1.5 text-sm text-success-text">
          <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
          You&apos;re all set.
        </p>
      )}
      {outstanding.length > 0 && (
        <ul className="space-y-2" aria-label="Still to do">
          {outstanding.map((item) => (
            <li key={item.key} className="flex items-start gap-2 text-sm">
              <CircleDashed className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
              <div className="space-y-0.5">
                {item.available && item.destination ? (
                  <Link to={item.destination} className="text-link underline underline-offset-2">
                    {item.actionLabel}
                  </Link>
                ) : (
                  <p>{item.actionLabel}</p>
                )}
                {item.note && <p className="text-xs text-muted-foreground">{item.note}</p>}
              </div>
            </li>
          ))}
        </ul>
      )}
      {done.length > 0 && (
        <Accordion type="single" collapsible>
          <AccordionItem value="done" className="border-b-0">
            <AccordionTrigger className="py-1">Done ({done.length})</AccordionTrigger>
            <AccordionContent>
              <ul className="space-y-1.5" aria-label="Done">
                {done.map((item) => (
                  <li key={item.key} className="flex items-center gap-2 text-sm text-foreground">
                    <CheckCircle2 className="h-4 w-4 shrink-0 text-success-text" aria-hidden="true" />
                    {item.label}
                  </li>
                ))}
              </ul>
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      )}
    </Section>
  );
}

function ExpandableText({ text }: { text: string }) {
  const [expanded, setExpanded] = useState(false);
  const textId = useId();
  if (text.length <= DESCRIPTION_PREVIEW_CHARS) {
    return <p className="text-sm whitespace-pre-wrap">{text}</p>;
  }
  // Cut at a word boundary; the full text is one click away and never lost.
  const cut = text.slice(0, DESCRIPTION_PREVIEW_CHARS);
  const preview = `${cut.slice(0, Math.max(cut.lastIndexOf(" "), DESCRIPTION_PREVIEW_CHARS - 40)).trimEnd()}…`;
  return (
    <div className="space-y-1">
      <p id={textId} className="text-sm whitespace-pre-wrap">{expanded ? text : preview}</p>
      <button
        type="button"
        className="text-xs text-link underline underline-offset-2"
        aria-expanded={expanded}
        aria-controls={textId}
        onClick={() => setExpanded((v) => !v)}
      >
        {expanded ? "Show less" : "Show more"}
      </button>
    </div>
  );
}

function Design({ appt }: { appt: AppointmentResponse }) {
  const { areaPhotoUrls, referenceUrls } = splitAppointmentImages(appt);
  const placement = appt.desiredPlacementLocations ?? [];
  const nothing = !appt.tattooDescription && !appt.style && placement.length === 0
    && areaPhotoUrls.length === 0 && referenceUrls.length === 0;

  return (
    <Section title="Your design" labelledBy="design-heading">
      {nothing ? (
        <p className="text-sm text-muted-foreground">No design details were added to this booking.</p>
      ) : (
        <>
          {appt.tattooDescription && <ExpandableText text={appt.tattooDescription} />}
          {(appt.style || placement.length > 0) && (
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
              {appt.style && (
                <>
                  <dt className="text-muted-foreground">Style</dt>
                  <dd>{appt.style}</dd>
                </>
              )}
              {placement.length > 0 && (
                <>
                  <dt className="text-muted-foreground">Placement</dt>
                  <dd>{placement.join(", ")}</dd>
                </>
              )}
            </dl>
          )}
          <AppointmentImageGallery label="Area photo" imageAlt="Area photo" urls={areaPhotoUrls} />
          <AppointmentImageGallery label="Reference images" imageAlt="Reference image" urls={referenceUrls} />
        </>
      )}
    </Section>
  );
}

// DEC11: the address arrives only for a Confirmed appointment; nothing about it is sent otherwise.
function Visit({ appt, visit }: { appt: AppointmentResponse; visit: ClientAppointmentVisit | null | undefined }) {
  let body: ReactNode;
  if (appt.status === AppointmentStatus.Pending) {
    body = (
      <p className="text-sm text-muted-foreground">
        The studio&apos;s address appears here once your booking is confirmed.
      </p>
    );
  } else if (visit?.addressComplete) {
    body = (
      <address className="not-italic text-sm space-y-0.5">
        <p>{visit.addressLine1}</p>
        {visit.addressLine2 && <p>{visit.addressLine2}</p>}
        <p>{[visit.postalCode, visit.city].filter(Boolean).join(" ")}</p>
      </address>
    );
  } else {
    body = (
      <p className="text-sm text-muted-foreground">
        The studio hasn&apos;t added its full address yet. Contact them below for directions.
      </p>
    );
  }
  return <Section title="Your visit" labelledBy="visit-heading">{body}</Section>;
}

function Aftercare({ aftercare, timezone }: { aftercare: ClientAppointmentAftercare; timezone: string | undefined }) {
  const sent = new Date(aftercare.sentAt).toLocaleDateString("en-GB", withStudioTimeZone({
    day: "numeric", month: "long", year: "numeric",
  }, timezone));
  return (
    <Section title="Aftercare" labelledBy="aftercare-heading">
      <p className="text-xs text-muted-foreground">The studio sent you these instructions on {sent}.</p>
      <ul className="list-disc space-y-1 pl-5 text-sm">
        {aftercare.steps.map((step) => <li key={step}>{step}</li>)}
      </ul>
    </Section>
  );
}

const MESSAGE_MAX = 2000;

function StudioDetails({ contact }: { contact: ClientAppointmentContact }) {
  const handle = contact.studioInstagram?.replace(/^@/, "");
  if (!contact.studioPhone && !handle) {
    return <p className="text-sm text-muted-foreground">{contact.studioName} hasn&apos;t added contact details yet.</p>;
  }
  return (
    <ul className="space-y-1 text-sm">
      {contact.studioPhone && (
        <li>
          Phone:{" "}
          <a className="text-link underline underline-offset-2" href={`tel:${contact.studioPhone}`}>{contact.studioPhone}</a>
        </li>
      )}
      {handle && (
        <li>
          Instagram:{" "}
          <a className="text-link underline underline-offset-2" href={`https://instagram.com/${handle}`} target="_blank" rel="noreferrer">
            @{handle}
          </a>
        </li>
      )}
    </ul>
  );
}

// DEC06: a message reaches the recipient the server resolved (the artist, else the studio owner).
// Nothing is sent until the client presses Send; with no recipient there is no compose box at all.
function Contact({ appointmentId, contact }: { appointmentId: string; contact: ClientAppointmentContact }) {
  const [body, setBody] = useState("");
  const [sentConversationId, setSentConversationId] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [createConversation, { isLoading: creating }] = useCreateConversationMutation();
  const [sendMessage, { isLoading: sending }] = useSendMessageMutation();
  const busy = creating || sending;
  const fieldId = useId();

  const recipient = contact.recipientRole === "artist" && contact.recipientName
    ? contact.recipientName
    : contact.studioName;

  async function handleSend() {
    const text = body.trim();
    if (!text || !contact.recipientUserId) return;
    setFailed(false);
    const conversation = await createConversation({ recipientUserId: contact.recipientUserId });
    if ("error" in conversation || !conversation.data) { setFailed(true); return; }
    const sent = await sendMessage({ conversationId: conversation.data.id, body: text, appointmentId });
    if ("error" in sent) { setFailed(true); return; }
    setBody("");
    setSentConversationId(conversation.data.id);
  }

  return (
    <Section title="Contact the studio" labelledBy="contact-heading">
      {contact.recipientUserId ? (
        <div className="space-y-2">
          <Label htmlFor={fieldId}>Message {recipient} about this appointment</Label>
          <Textarea
            id={fieldId}
            value={body}
            maxLength={MESSAGE_MAX}
            onChange={(e) => { setBody(e.target.value); setSentConversationId(null); }}
            placeholder="Your message"
          />
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="gap-1.5"
            disabled={busy || body.trim().length === 0}
            onClick={() => void handleSend()}
          >
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Send className="h-3.5 w-3.5" />}
            Send
          </Button>
          <div aria-live="polite">
            {sentConversationId && (
              <p className="text-sm text-success-text">
                Message sent.{" "}
                <Link
                  className="text-link underline underline-offset-2"
                  to={`/messages?conversation=${sentConversationId}`}
                >
                  Open the conversation
                </Link>
              </p>
            )}
          </div>
          {failed && (
            <p role="alert" className="text-sm text-destructive-text">
              Your message wasn&apos;t sent. Please try again, or contact the studio directly.
            </p>
          )}
          {(contact.studioPhone || contact.studioInstagram) && (
            <div className="pt-1 text-xs text-muted-foreground space-y-1">
              <p>Or contact {contact.studioName} directly:</p>
              <StudioDetails contact={contact} />
            </div>
          )}
        </div>
      ) : (
        <div className="space-y-2">
          <p className="text-sm">Messaging isn&apos;t available for this studio, so please contact {contact.studioName} directly.</p>
          <StudioDetails contact={contact} />
        </div>
      )}
    </Section>
  );
}

function ChangeOrCancel({ appt }: { appt: AppointmentResponse }) {
  // Behind a disclosure so it is easy to find but never competes with the next step.
  return (
    <section aria-label="Change or cancel">
      <Accordion type="single" collapsible className="rounded-xl border bg-card px-4">
        <AccordionItem value="change" className="border-b-0">
          <AccordionTrigger>Need to change or cancel?</AccordionTrigger>
          <AccordionContent className="text-foreground space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              <RescheduleArea appt={appt} />
              <CancelArea appt={appt} />
            </div>
          </AccordionContent>
        </AccordionItem>
      </Accordion>
    </section>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────

function AppointmentView({
  data, refreshing, refreshFailed, onRetry,
}: {
  data: ClientAppointmentDetailResponse;
  refreshing: boolean;
  refreshFailed: boolean;
  onRetry: () => void;
}) {
  const { data: studio } = useGetMyStudioQuery();
  const appt = data.appointment;
  const upcoming = UPCOMING.has(appt.status);

  // The server lists requirements in priority order; the first outstanding one the client can act on
  // is the next step. Nothing outstanding → no CTA at all (never a disabled one).
  const next = upcoming ? data.readiness.find((i) => !i.satisfied && i.available) : undefined;

  return (
    <div className="space-y-4">
      <p className="sr-only" aria-live="polite">{refreshing ? "Updating appointment…" : ""}</p>
      {refreshing && (
        <p className="flex items-center gap-1.5 text-xs text-muted-foreground" aria-hidden="true">
          <Loader2 className="h-3 w-3 animate-spin" />
          Updating…
        </p>
      )}
      {refreshFailed && (
        <p role="alert" className="text-xs text-destructive-text">
          Couldn&apos;t refresh this appointment — what you see may be out of date.{" "}
          <button type="button" className="underline underline-offset-2" onClick={onRetry}>Try again</button>
        </p>
      )}
      <Summary
        appt={appt}
        depositIsNextAction={next?.key === ReadinessKey.DepositPaid}
        timezone={studio?.timezone}
      />
      {next && <NextAction appt={appt} item={next} />}
      {upcoming && data.readiness.length > 0 && <Preparation items={data.readiness} next={next} />}
      {appt.status === AppointmentStatus.Completed && data.aftercare && (
        <Aftercare aftercare={data.aftercare} timezone={studio?.timezone} />
      )}
      <Design appt={appt} />
      {upcoming && <Visit appt={appt} visit={data.visit} />}
      <Contact appointmentId={appt.id} contact={data.contact} />
      {upcoming && <ChangeOrCancel appt={appt} />}
    </div>
  );
}

export function ClientAppointmentDetailPage() {
  useDocumentMeta({ title: "Your appointment — TattooOS", canonical: "/my-appointments" });
  const { id = "" } = useParams<{ id: string }>();
  // currentData: this appointment's last result, kept through a background refetch (and never another
  // appointment's, if the route changes).
  const { currentData, error, isFetching, isError, refetch } = useGetMyAppointmentQuery(id, { skip: !id });
  const retry = () => void refetch();

  let body: ReactNode;
  if (currentData) {
    body = (
      <AppointmentView
        data={currentData}
        refreshing={isFetching}
        refreshFailed={isError && !isFetching}
        onRetry={retry}
      />
    );
  } else if (isFetching) {
    body = <PageSkeleton />;
  } else if (isError && errorStatus(error) === 404) {
    body = <NotFoundState />;
  } else if (isError) {
    body = <LoadFailedState onRetry={retry} retrying={isFetching} />;
  } else {
    body = <NotFoundState />;
  }

  return <PageShell>{body}</PageShell>;
}
