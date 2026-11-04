import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { ImageIcon } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import {
  Accordion, AccordionContent, AccordionItem, AccordionTrigger,
} from "@/shared/components/ui/accordion";
import { formatSessionSpan } from "@/shared/utils/studioDay";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { DepositStatusBadge } from "@/features/appointments/components/DepositStatusBadge";
import { AppointmentImageGallery } from "@/features/appointments/components/AppointmentImageGallery";
import { splitAppointmentImages } from "@/features/appointments/appointmentImages";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { SessionPreparation } from "./SessionPreparation";

/** A brief longer than this collapses behind "Show more" inside the disclosure. */
export const BRIEF_PREVIEW_CHARS = 280;
/** The one-line teaser of the brief on the card itself. */
const BRIEF_TEASER_CHARS = 90;

function cutAtWord(text: string, max: number): string {
  if (text.length <= max) return text;
  const cut = text.slice(0, max);
  return `${cut.slice(0, Math.max(cut.lastIndexOf(" "), max - 30)).trimEnd()}…`;
}

function ExpandableBrief({ text }: { text: string }) {
  const [expanded, setExpanded] = useState(false);
  const textId = useId();
  if (text.length <= BRIEF_PREVIEW_CHARS) return <p className="text-sm whitespace-pre-wrap break-words">{text}</p>;
  return (
    <div className="space-y-1">
      <p id={textId} className="text-sm whitespace-pre-wrap break-words">
        {expanded ? text : cutAtWord(text, BRIEF_PREVIEW_CHARS)}
      </p>
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

/** "Preparation: 2 of 4 done". Nothing when the service configures no requirements (never "0 of 0"). */
function ReadinessLine({ appt }: { appt: AppointmentResponse }) {
  const r = appt.readiness;
  if (!r) return <span className="text-muted-foreground">Preparation: not available</span>;
  if (r.required === 0) return null;
  return r.satisfied >= r.required
    ? <span className="text-success-text">Preparation: all {r.required} done</span>
    : <span className="text-warning-text">Preparation: {r.satisfied} of {r.required} done</span>;
}

export function TodaySessionCard({ appt, timeZone }: { appt: AppointmentResponse; timeZone: string | undefined }) {
  const headingId = useId();
  const { referenceUrls, areaPhotoUrls } = splitAppointmentImages(appt);
  const placement = appt.desiredPlacementLocations ?? [];
  const span = formatSessionSpan(appt.date, appt.endDate, timeZone);

  return (
    <Card aria-labelledby={headingId} role="article">
      <CardContent className="p-4 space-y-2">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0 space-y-1">
            <p className="text-sm font-medium" data-testid="session-span">{span}</p>
            <h2 id={headingId} className="text-base font-semibold break-words">
              {appt.clientName ?? "Client"}
            </h2>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <AppointmentStatusBadge status={appt.status} />
            <Link
              to={`/appointments/${appt.id}`}
              className="text-sm text-link underline underline-offset-2"
            >
              Open appointment
            </Link>
          </div>
        </div>

        {appt.serviceName && <p className="text-sm text-muted-foreground break-words">{appt.serviceName}</p>}
        {appt.tattooDescription && (
          <p className="text-sm break-words">{cutAtWord(appt.tattooDescription, BRIEF_TEASER_CHARS)}</p>
        )}

        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs" aria-label="Session details">
          <li>{placement.length > 0 ? <>Placement: {placement.join(", ")}</> : <span className="text-muted-foreground">No placement given</span>}</li>
          <li className="flex items-center gap-1">
            <ImageIcon aria-hidden="true" className="h-3.5 w-3.5 text-muted-foreground" />
            {referenceUrls.length > 0
              ? `${referenceUrls.length} reference image${referenceUrls.length === 1 ? "" : "s"}`
              : <span className="text-muted-foreground">No reference images</span>}
          </li>
          {appt.depositAmount > 0 && (
            <li className="flex items-center gap-1">Deposit <DepositStatusBadge status={appt.depositStatus} /></li>
          )}
          {appt.readiness?.required !== 0 && <li><ReadinessLine appt={appt} /></li>}
        </ul>

        <Accordion type="single" collapsible>
          <AccordionItem value="prepare" className="border-b-0">
            <AccordionTrigger className="py-2 text-sm">Brief and preparation</AccordionTrigger>
            <AccordionContent className="space-y-3 text-foreground">
              <section aria-label="Brief" className="space-y-2">
                {appt.tattooDescription
                  ? <ExpandableBrief text={appt.tattooDescription} />
                  : <p className="text-sm text-muted-foreground">No brief was added to this booking.</p>}
                {(appt.style || placement.length > 0) && (
                  <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-sm">
                    {appt.style && (<><dt className="text-muted-foreground">Style</dt><dd>{appt.style}</dd></>)}
                    {placement.length > 0 && (<><dt className="text-muted-foreground">Placement</dt><dd>{placement.join(", ")}</dd></>)}
                  </dl>
                )}
                <AppointmentImageGallery label="Area photo" imageAlt="Area photo" urls={areaPhotoUrls} />
                <AppointmentImageGallery label="Reference images" imageAlt="Reference image" urls={referenceUrls} />
              </section>
              <SessionPreparation appt={appt} />
              <p className="text-xs text-muted-foreground">
                Notes, safety notes and the forms themselves are on the{" "}
                <Link to={`/appointments/${appt.id}`} className="text-link underline underline-offset-2">appointment page</Link>.
              </p>
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      </CardContent>
    </Card>
  );
}
