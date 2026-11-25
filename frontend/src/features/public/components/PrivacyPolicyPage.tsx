import { Link } from "react-router-dom";
import { PublicContentLayout } from "./PublicContentLayout";
import { LawyerReviewBanner } from "./LawyerReviewBanner";
import {
  LEGAL_ENTITY_NIPT,
  LEGAL_ENTITY_ADDRESS,
} from "@/shared/constants/legalEntity";
import { ROUTE_META } from "@/shared/seo/siteRoutes";
import { LegalVersionLine } from "../legal/LegalClauses";
import { LEGAL_VERSION, LEGAL_LAST_UPDATED, LEGAL_CONTACT_EMAIL } from "../legal/legalMeta";

function Section({ heading, children }: { heading: string; children: React.ReactNode }) {
  return (
    <section className="mt-6">
      <h2 className="text-lg font-semibold tracking-tight">{heading}</h2>
      <div className="mt-2 space-y-2 text-sm text-muted-foreground">{children}</div>
    </section>
  );
}

// Final text signed off 2026-11-25, then adjusted so every statement matches what the product does today
// (health-consent tick, age check and a fixed log-retention period are NOT built yet, so they are not claimed).
// Content bar: Albania's Law 124/2024 on Personal Data Protection. Keep this page truthful to the code.
export function PrivacyPolicyPage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/privacy"].title}
      description={ROUTE_META["/privacy"].description}
      canonicalPath="/privacy"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Privacy Policy</h1>
      <LegalVersionLine lastUpdated={LEGAL_LAST_UPDATED} version={LEGAL_VERSION} />

      <div className="mt-6">
        <LawyerReviewBanner doc="privacy" />
      </div>

      <Section heading="1. Who we are and our two roles">
        <p>
          TattooOS is operated by the business registered in Albania under NIPT {LEGAL_ENTITY_NIPT},
          {" "}{LEGAL_ENTITY_ADDRESS}. For privacy requests, write to {LEGAL_CONTACT_EMAIL} or use the{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            Contact
          </Link>{" "}
          page. Our data-protection contact is the operator, at that address.
        </p>
        <ul className="list-disc space-y-1 pl-5">
          <li>
            <strong>The studio is the controller</strong> of your booking, consent-form, body-map and
            health data. It decides why and how it uses them. <strong>TattooOS acts as the studio&apos;s
            processor</strong> for that data and handles it only on the studio&apos;s instructions.
          </li>
          <li>
            <strong>TattooOS is the controller</strong> of the login details of studio staff and clients,
            billing data of studios, the website and contact form, security and error logs, and the
            optional portable tattoo profile described in section 3.
          </li>
        </ul>
        <p>
          For a request about records held by a studio, contact the studio first. We will help route and
          carry it out.
        </p>
      </Section>

      <Section heading="2. Data we handle">
        <ul className="list-disc space-y-1 pl-5">
          <li>Account and contact data: name, email, phone, password hash, role.</li>
          <li>
            Booking data: studio, artist, service, date and time, tattoo description, style, placement,
            how you heard of the studio, notes, codes, deposit status.
          </li>
          <li>Consent forms, body maps, designs, reference and portfolio images.</li>
          <li>
            <strong>Health data (special category):</strong> allergies, medical conditions, medication and
            similar notes, only if you enter them. This includes the optional safety field on every
            booking request, including a guest booking, as well as a client profile and consent forms.
          </li>
          <li>
            Payment data: amount, status and provider reference. Card numbers go to our payment partners
            and we never see or store them.
          </li>
          <li>Messages between you and your studio.</li>
          <li>Technical data: IP address, device and browser type, request and error logs.</li>
          <li>
            Optional Instagram data for artists who connect it: username, recent public photos and an
            encrypted access token.
          </li>
        </ul>
      </Section>

      <Section heading="3. Optional portable profile and Instagram">
        <p>
          <strong>Portable profile.</strong> If you choose, you can share your tattoo history (body-map
          locations, tattoo photos and descriptions) with other studios on TattooOS. This is a separate,
          explicit choice you can withdraw at any time. It never includes health information, contact
          details or payment history.
        </p>
        <p>
          <strong>Instagram.</strong> If an artist connects an Instagram Business or Creator account, we
          receive the account&apos;s username and recent photo posts through Instagram&apos;s official API,
          and store an encrypted access token. We use them only to show those photos on the artist&apos;s
          public portfolio and to display a Verified badge; we never post on the artist&apos;s behalf.
          Disconnecting in TattooOS, or removing TattooOS under Instagram Settings &rarr; Apps and
          websites, stops all access and deletes the stored token and the synced photos: in TattooOS
          immediately, and on Instagram&apos;s side as soon as Instagram notifies us of the removal. If you
          want your Instagram data deleted another way, contact us via the{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            Contact
          </Link>{" "}
          page.
        </p>
      </Section>

      <Section heading="4. Why we use data and our legal basis">
        <ul className="list-disc space-y-1 pl-5">
          <li>Run your account, take and manage bookings and deposits: contract, and steps you request before a contract.</li>
          <li>Save your unfinished booking draft for 7 days: steps you request before a contract.</li>
          <li>
            Use the health information you choose to enter so the artist can tattoo you safely: your
            consent. Entering it is optional, it is seen only by the studio you give it to, and you can
            withdraw consent at any time by asking the studio to delete it.
          </li>
          <li>Optional portable profile and marketing messages: your consent.</li>
          <li>Keep the service secure, prevent fraud, fix errors: legitimate interest.</li>
          <li>Tax, accounting and fiscal invoices: legal obligation.</li>
          <li>Defend legal claims, including keeping consent forms: legitimate interest and defence of legal claims.</li>
        </ul>
      </Section>

      <Section heading="5. Booking drafts">
        <p>
          If you start a booking while signed in, we save your draft for 7 days so you can finish later.
          A draft never contains the health information you type in the safety field. You can delete a
          draft at any time, and a draft is deleted when you delete your account.
        </p>
        <p>
          If you book as a guest with an email address that already belongs to an account, we do not
          create the booking. What you typed is held for 7 days as an unclaimed draft. The account holder
          cannot see it until they follow a link in the email we send and sign in. If they never do, it is
          deleted. The health information in the safety field is not kept in it.
        </p>
      </Section>

      <Section heading="6. Who sees your data">
        <p>
          The studio you book with and its staff, and never other studios, except what you share through
          the portable profile. Our service providers (processors) listed below. Authorities, only when
          the law requires. We do not sell personal data.
        </p>
      </Section>

      <Section heading="7. Service providers and transfers">
        <ul className="list-disc space-y-1 pl-5">
          <li>Cloudflare R2: file and image storage and delivery</li>
          <li>Resend: transactional email (including relaying contact-form messages)</li>
          <li>Twilio: SMS notifications</li>
          <li>Hetzner (Germany): application hosting</li>
          <li>DigitalOcean (Frankfurt): managed database</li>
          <li>Meta (Instagram): optional account connection and Verified badge for artists</li>
          <li>
            Payment provider (POK): client card deposits, which are paid to the studio&apos;s own account,
            and studio subscription payments where the studio pays by card
          </li>
        </ul>
        <p>
          <strong>Transfers.</strong> Some of these providers are located outside Albania and the EEA,
          including in the United States. Albania has no EU adequacy decision. We transfer data outside
          Albania and the EEA only where the receiving provider is covered by a recognised adequacy
          arrangement (for example the EU-US Data Privacy Framework where it is certified), or under
          standard contractual clauses with the provider, with extra safeguards where needed. The health
          notes you enter are not included in our emails or text messages. You may ask us for a copy of
          the safeguards.
        </p>
        <p>
          <strong>Contact form.</strong> When you use our contact form, your name, email, and message are
          relayed to our support inbox by email (via Resend) so we can reply. Contact submissions are{" "}
          <strong>not stored in our database</strong> beyond that email delivery.
        </p>
      </Section>

      <Section heading="8. How long we keep data">
        <ul className="list-disc space-y-1 pl-5">
          <li>
            Consent forms and body maps: 7 years after your last appointment or account closure, because
            the studio may need them to establish or defend a legal claim. They are then soft-deleted and,
            after a 30-day grace window, permanently purged.
          </li>
          <li>
            Health notes you type at booking are stored with the booking record. Erasing your data
            deletes them.
          </li>
          <li>Booking drafts: 7 days.</li>
          <li>
            Account data: while the account is open. After an erasure request we delete it within 30
            days, unless the law or a legal claim requires us to keep it. Logging in is disabled
            immediately.
          </li>
          <li>Tax and fiscal records: the period the law requires.</li>
          <li>Server logs are kept only for a short period, for security and to fix faults.</li>
          <li>
            Database backups are kept for up to 7 days. Data you have erased can remain in them until they
            expire, and is not restored except after a disaster, when erasures are applied again.
          </li>
        </ul>
        <p>
          If you ask us to erase data that we must keep for legal claims or the law, we tell you what we
          kept and why, and delete the rest.
        </p>
      </Section>

      <Section heading="9. Your rights">
        <p>
          Under Law 124/2024 you have the right to access, correct, erase, restrict, receive in portable
          form, or object to our use of your personal data, and to withdraw consent at any time.
          Withdrawing consent is as easy as giving it. We answer within 30 days. If the request is complex
          we may extend this by up to 60 more days and will tell you why. Write to {LEGAL_CONTACT_EMAIL} or
          use the{" "}
          <Link to="/contact" className="underline underline-offset-2 hover:text-foreground">
            Contact
          </Link>{" "}
          page.
        </p>
      </Section>

      <Section heading="10. Complaints">
        <p>
          You may complain to the Information and Data Protection Commissioner of Albania (Komisioneri për
          të Drejtën e Informimit dhe Mbrojtjen e të Dhënave Personale). If you live in the EU or EEA you
          may also contact your local data protection authority.
        </p>
      </Section>

      <Section heading="11. Security">
        <p>
          We separate each studio&apos;s data, use encrypted connections, limit staff access, log access
          without personal data, and test our controls. No system is perfectly secure. We report breaches
          to the Commissioner and to those affected as the law requires.
        </p>
      </Section>

      <Section heading="12. Children">
        <p>
          TattooOS is for people aged 18 and over. A studio may accept clients aged 14 to 17 only with a
          parent or guardian who gives consent in person. We do not knowingly collect data from anyone
          under 14. If we learn we have, we delete it.
        </p>
      </Section>

      <Section heading="13. Cookies">
        <p>
          We use the cookies and local storage needed to run the service, such as your login and booking
          state. Where we use any other cookie, we ask for your consent first.
        </p>
      </Section>

      <Section heading="14. Changes">
        <p>
          We tell account holders by email and in the app before a material change. The date at the top
          shows the latest version.
        </p>
      </Section>
    </PublicContentLayout>
  );
}
