import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { LawyerReviewBanner } from "./LawyerReviewBanner";
import {
  LEGAL_ENTITY_NAME,
  LEGAL_ENTITY_NIPT,
  LEGAL_ENTITY_ADDRESS,
} from "@/shared/constants/legalEntity";

function Section({ heading, children }: { heading: string; children: React.ReactNode }) {
  return (
    <section className="mt-6">
      <h2 className="text-lg font-semibold tracking-tight">{heading}</h2>
      <div className="mt-2 space-y-2 text-sm text-muted-foreground">{children}</div>
    </section>
  );
}

// Structural outline only — binding text pending lawyer review (open question §3.4).
// Content bar set by GDPR Art. 13/14 and Albania's Law 124/2024 on Personal Data
// Protection. Truthful to what the app actually does; not marketing copy.
export function PrivacyPolicyPage() {
  return (
    <PublicContentLayout
      title="Privacy Policy — TattooOS"
      description="How TattooOS collects, uses, and protects your personal data."
      canonicalPath="/privacy"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Privacy Policy</h1>
      <p className="mt-2 text-sm text-muted-foreground">
        TattooOS is operated by {LEGAL_ENTITY_NAME} (NIPT {LEGAL_ENTITY_NIPT}),
        {" "}{LEGAL_ENTITY_ADDRESS} — the data controller for the personal data described
        below.
      </p>

      <div className="mt-6">
        <LawyerReviewBanner />
      </div>

      <Section heading="1. Personal data we collect">
        <p>
          Account and contact details (name, email, phone), booking and appointment
          records, uploaded designs and portfolio images, and signed consent records.
        </p>
        <p>
          <strong>Special-category (health) data.</strong> If you complete a client
          profile, we may process medical notes and allergies you provide. This is
          special-category data under Art. 9 GDPR / Law 124/2024. It is used only by the
          studio you provide it to and is <strong>not</strong> shared with other studios.
        </p>
        <p>
          <strong>Portable profile (optional).</strong> You may opt in to share your tattoo
          history — body-map locations, tattoo photos, and descriptions — with other studios
          on TattooOS, via a separate, explicit consent you can withdraw at any time. This
          sharing never includes your medical notes, allergies, contact details, or payment
          history.
        </p>
      </Section>

      <Section heading="2. Purposes and legal basis">
        <p>
          Performance of a contract (managing your bookings, deposits and consent
          records); legitimate interests (securing the service, fraud prevention);
          consent (special-category health data, optional cross-studio profile sharing,
          marketing where applicable); and legal obligation (tax/accounting records).
        </p>
      </Section>

      <Section heading="3. Data retention">
        <p>
          We keep records only as long as necessary for the purposes above and applicable
          law. Consent forms and body-map data are retained for 7 years after your last
          appointment or account closure — the standard body-art record-retention period —
          then soft-deleted and, after a 30-day grace window, permanently purged.
        </p>
      </Section>

      <Section heading="4. Sub-processors">
        <ul className="list-disc space-y-1 pl-5">
          <li>Cloudflare R2 — file/image storage and CDN</li>
          <li>Resend — transactional email</li>
          <li>Twilio — SMS notifications</li>
          <li>Our hosting provider — application and database hosting</li>
          <li>
            Payment providers (POK, easyPos, Polar) — <em>planned, not yet live;</em>{" "}
            listed here for transparency ahead of launch
          </li>
        </ul>
      </Section>

      <Section heading="5. Your rights">
        <p>
          Under Law 124/2024 and the GDPR you have the right to access, rectify, erase,
          restrict, port, and object to processing of your personal data, and to withdraw
          consent at any time — withdrawal is as easy to exercise as giving consent. To
          request erasure of your data, contact us via the{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            Contact
          </Link>{" "}
          page.
        </p>
      </Section>

      <Section heading="6. Controller and contact">
        <p>
          Data controller: {LEGAL_ENTITY_NAME} (NIPT {LEGAL_ENTITY_NIPT}),
          {" "}{LEGAL_ENTITY_ADDRESS}. For any privacy request or to reach our
          data-protection contact, see the{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            Contact
          </Link>{" "}
          page.
        </p>
      </Section>
    </PublicContentLayout>
  );
}
