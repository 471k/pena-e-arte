import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "@/shared/components/ui/accordion";

interface FaqEntry {
  id: string;
  question: string;
  answer: ReactNode;
}

const LINK_CLASS = "underline underline-offset-2 hover:text-foreground";

// Only answers that are true in the shipped product. A question that cannot be answered
// from verified behaviour is left out rather than guessed.
const FAQ_ENTRIES: ReadonlyArray<FaqEntry> = [
  {
    id: "what-is-tattooos",
    question: "What is TattooOS?",
    answer:
      "TattooOS is booking and studio-management software for tattoo studios: online booking, deposits, digital consent forms, design approvals and client records in one place.",
  },
  {
    id: "commission",
    question: "Does TattooOS take a commission on bookings?",
    answer:
      "No. Clients pay the studio, not TattooOS, and TattooOS charges no commission on bookings. Studios pay a subscription for the software.",
  },
  {
    id: "client-account",
    question: "Do clients need an account to book?",
    answer:
      "No. Clients can book as a guest without signing up first. A client who already has an account is asked to sign in.",
  },
  {
    id: "deposits",
    question: "How do deposits work?",
    answer: (
      <>
        Each studio sets its own deposit rules, and clients see what is due while booking.
        Cash deposits are marked as received by the artist or owner, and card deposits go to the
        studio&apos;s own payment account where one is connected.{" "}
        <Link to="/use/deposits" className={LINK_CLASS}>
          More about deposits
        </Link>
        .
      </>
    ),
  },
  {
    id: "consent-forms",
    question: "How do digital consent forms work?",
    answer: (
      <>
        Clients sign the studio&apos;s intake and consent forms before the appointment. Each
        signature is timestamped and stored with the exact wording the client agreed to.{" "}
        <Link to="/use/consent-forms" className={LINK_CLASS}>
          More about consent forms
        </Link>
        .
      </>
    ),
  },
  {
    id: "get-started",
    question: "How do I get started?",
    answer: (
      <>
        <Link to="/register" className={LINK_CLASS}>
          Register your studio
        </Link>{" "}
        and set up your artists and deposit rules. See the{" "}
        <Link to="/pricing" className={LINK_CLASS}>
          pricing page
        </Link>{" "}
        for the plans.
      </>
    ),
  },
];

export function FaqPage() {
  return (
    <PublicContentLayout
      title="FAQ — TattooOS"
      description="Answers to common questions about TattooOS: commission, deposits, consent forms, guest booking and getting started."
      canonicalPath="/faq"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Frequently asked questions</h1>

      <Accordion type="single" collapsible className="mt-8">
        {FAQ_ENTRIES.map((entry) => (
          <AccordionItem key={entry.id} value={entry.id}>
            <AccordionTrigger>{entry.question}</AccordionTrigger>
            <AccordionContent className="text-muted-foreground">{entry.answer}</AccordionContent>
          </AccordionItem>
        ))}
      </Accordion>

      <MarketingCtaRow />
    </PublicContentLayout>
  );
}
