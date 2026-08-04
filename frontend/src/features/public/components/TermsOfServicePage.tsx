import { PublicContentLayout } from "./PublicContentLayout";
import { LawyerReviewBanner } from "./LawyerReviewBanner";
import { LEGAL_ENTITY_NAME, LEGAL_ENTITY_ADDRESS } from "@/shared/constants/legalEntity";

function Section({ heading, children }: { heading: string; children: React.ReactNode }) {
  return (
    <section className="mt-6">
      <h2 className="text-lg font-semibold tracking-tight">{heading}</h2>
      <p className="mt-2 text-sm text-muted-foreground">{children}</p>
    </section>
  );
}

// Structural outline only — binding text pending lawyer review (open question §3.4).
export function TermsOfServicePage() {
  return (
    <PublicContentLayout
      title="Terms of Service — TattooOS"
      description="The terms governing your use of TattooOS."
      canonicalPath="/terms"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Terms of Service</h1>
      <p className="mt-2 text-sm text-muted-foreground">
        These terms govern use of the TattooOS platform, operated by {LEGAL_ENTITY_NAME},
        {" "}{LEGAL_ENTITY_ADDRESS}.
      </p>

      <div className="mt-6">
        <LawyerReviewBanner />
      </div>

      <Section heading="1. The service">
        TattooOS provides booking, deposit, consent-form, and studio-management tooling
        for tattoo studios and their clients.
      </Section>
      <Section heading="2. Accounts and eligibility">
        You are responsible for the accuracy of your account information and for
        safeguarding your credentials.
      </Section>
      <Section heading="3. Bookings, deposits and payments">
        Deposits, cancellations, no-shows and refunds are governed by each studio&apos;s
        configured policy, summarised on our Refund Policy page.
      </Section>
      <Section heading="4. Acceptable use">
        You may not misuse the service, attempt to access other tenants&apos; data, or use
        it for unlawful purposes.
      </Section>
      <Section heading="5. Liability and changes">
        Liability limitations and the process for changes to these terms will be set out
        in the final reviewed text.
      </Section>
    </PublicContentLayout>
  );
}
