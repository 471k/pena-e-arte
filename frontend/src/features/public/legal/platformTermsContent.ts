import type { LegalClause } from "./legalTypes";
import { LEGAL_ENTITY_NIPT, LEGAL_ENTITY_ADDRESS } from "@/shared/constants/legalEntity";
import { LEGAL_CONTACT_EMAIL } from "./legalMeta";

export const PLATFORM_TERMS_CLAUSES: ReadonlyArray<LegalClause> = [
  {
    heading: "1. Who we are and what these terms cover",
    paragraphs: [
      `TattooOS is operated by the business registered in Albania under NIPT ${LEGAL_ENTITY_NIPT}, ${LEGAL_ENTITY_ADDRESS} (“TattooOS”, “we”). These terms are the contract between TattooOS and the tattoo studio that opens an account (“you”, “the studio”). Clients have their own terms.`,
    ],
  },
  {
    heading: "2. The service",
    paragraphs: [
      "TattooOS is software for studios: booking, deposits, consent forms, design approval, client records, messaging and reporting. We provide software. We are not a party to the tattooing service between you and your client, and we do not give medical, tax, legal or licensing advice.",
    ],
  },
  {
    heading: "3. Business use only",
    paragraphs: ["You confirm you use TattooOS for a business or profession. You are not a consumer for these terms."],
  },
  {
    heading: "4. Your account",
    paragraphs: [
      "The person who opens the account must be able to bind the studio and be at least 18. Give true information and keep it current. You are responsible for everything done under your studio’s logins, for the people you invite, and for removing access when someone leaves. Tell us at once if a login is compromised.",
    ],
  },
  {
    heading: "5. Trial, grace period and suspension",
    paragraphs: [
      "A new studio gets a 14-day trial with all features and no card. If you do not choose a plan by the end of the trial, the studio goes into a 7-day read-only period: you can see and export your data but cannot create bookings. If you still have not chosen a plan after those 7 days, we suspend the account until you do. We tell you before we delete any data held for a suspended account.",
    ],
  },
  {
    heading: "6. Plans, fees and renewal",
    paragraphs: [
      "Plans and what each includes are shown on the pricing page. Prices are in lek and are final prices: they include VAT where VAT applies. The monthly prices are: Free 0, Solo 2,000, Studio 4,500 and Studio Plus 9,000 ALL. Monthly plans are billed each month in advance. Yearly plans are billed once a year in advance and cost ten times the monthly price (two months free). There are no setup fees, no per-booking fees and no commission on your deposits.",
      "Paid plans renew automatically each month or year, by card on file through our payment partner (POK) once that option is available to you, or against our invoice if you pay by bank transfer. If a card payment fails we retry on days 1, 3 and 7. If it still fails the account becomes read-only and may be suspended.",
      "The price in force when you subscribe or renew is the price you pay for that period. We will tell you at least 30 days before any price change takes effect, and a change never applies to a period you have already paid. If you do not accept a change you may cancel before it takes effect.",
    ],
  },
  {
    heading: "7. Taxes and invoices",
    paragraphs: [
      "We issue a fiscal e-invoice for every charge, as Albanian law requires. Your studio’s own taxes on your income, including VAT on your services to clients, are your responsibility.",
    ],
  },
  {
    heading: "8. Yearly plans: cancelling and refunds",
    bullets: [
      "(a) You may cancel a yearly plan at any time. Access ends the day you cancel.",
      "(b) We refund: the amount you actually paid for the current year, minus (months started × the monthly price in force when that year was bought or renewed), and never less than zero. A month that has started counts as a full month. Cancelling on day one counts as one month.",
      "(c) Example: a Studio yearly plan costs 45,000 ALL, with a monthly price of 4,500 ALL. Cancelling in month 1 refunds 40,500, in month 3 refunds 31,500, in month 6 refunds 18,000, and from month 10 refunds nothing.",
      "(d) Account credit from referral rewards is not cash. It is not refunded and it ends when you cancel.",
      "(e) A failed yearly renewal ends the plan with no refund. If we suspend the studio for breaking these terms, the suspended time counts as used and no refund is due unless we decide otherwise in writing.",
      "(f) We cancel any card subscription first, so it cannot charge you again. We then pay the refund by bank transfer to an account in the studio’s own name, within 14 days of confirming the cancellation. We issue a corrective fiscal invoice for the refunded amount.",
      "(g) We show you the exact refund amount before you confirm the cancellation, and repeat it in the confirmation email.",
    ],
  },
  {
    heading: "9. Monthly plans",
    paragraphs: ["You may cancel at any time. We do not refund the month already paid. Access continues until the end of that month."],
  },
  {
    heading: "10. Referral rewards",
    paragraphs: [
      "Rewards and credits are promotional, have no cash value, cannot be transferred or sold, and may be withdrawn if they were obtained by abuse or false accounts. Their terms are shown where the offer is made.",
    ],
  },
  {
    heading: "11. How deposit money moves",
    paragraphs: [
      "Client card deposits are paid to your own account with our payment partner (POK) under your agreement with that provider. TattooOS does not hold, pool or pay out your money and takes no commission. Card charges made by the payment partner are yours to bear. Refunds to clients are paid from your balance with the payment partner, so you must keep enough balance, and an automatic withdrawal that empties it can leave it negative when a refund lands. Chargebacks are handled between you and your payment partner, which charges a fee for each one (currently 75 EUR) that is yours to bear. Cash deposits are recorded by you and settled with the client directly. Subscription fees to TattooOS are paid as set out in clause 6 and are separate from your deposits. We add no surcharge to client card payments.",
    ],
  },
  {
    heading: "12. Your responsibilities as a studio",
    bullets: [
      "You hold every licence, registration, insurance and health-and-hygiene approval that your activity needs in your location.",
      "You decide your deposit, cancellation and age policies and apply them fairly. You give your clients the information the law requires before they pay, including that no 14-day withdrawal right applies to a booking for a fixed date.",
      "You decide whom you tattoo and answer for it. You never tattoo anyone you are not legally allowed to, and never anyone under 14. For a client aged 14 to 17 you get a parent or guardian’s consent in person.",
      "You check the client’s identity and age in person.",
      "You use consent forms and health information only to carry out the tattoo safely, and you protect them.",
      "You keep your own accounting and tax records, and you issue your own fiscal invoices to your clients. Our payment receipts are not tax invoices.",
    ],
  },
  {
    heading: "13. Client data and data protection",
    paragraphs: [
      "For the personal data of your clients (bookings, consent forms, body maps, health information, messages) you are the controller and we are your processor. We process it only on your documented instructions: to run the service. The Data Processing Terms at the end of this document form part of these terms. For the data of your own staff, your billing data and our security logs, we are a controller. See our Privacy Policy.",
      "You are responsible for having a lawful basis, giving your clients the notice they need and answering their requests. We help you do that. Consent forms and body maps are kept for 7 years after the last appointment, as our Privacy Policy explains.",
    ],
  },
  {
    heading: "14. Content and licences",
    paragraphs: [
      "You keep ownership of your content: designs, photos, portfolio, text. You give us a limited licence to host, copy, display and process it to provide the service. You promise you have the right to upload it and that it does not infringe anyone’s rights. You may export your data at any time.",
    ],
  },
  {
    heading: "15. Acceptable use",
    paragraphs: [
      "You may not: break the law; use another studio’s or client’s data without right; try to reach other studios’ data or probe our systems; upload malware or unlawful, hateful or exploitative content; tattoo or arrange tattooing of anyone under the age rules above; resell the service; or use the platform to harass clients. We may remove content or suspend an account that breaks this clause. We will warn you first unless the risk is serious or urgent.",
    ],
  },
  {
    heading: "16. Availability, support and changes",
    paragraphs: [
      `We work to keep the service running and fix faults quickly, but we do not promise it will never be interrupted. We announce planned maintenance in advance. Support is by email (${LEGAL_CONTACT_EMAIL}) and in-app chat. Chat is staffed Monday to Friday, 10:00 to 18:00 Tirana time, and email is answered within one business day.`,
      "We may change or retire features. If a change removes a core feature of your plan, we give 30 days’ notice and a pro-rata refund of the unused paid period if you cancel because of it.",
    ],
  },
  {
    heading: "17. Liability",
    paragraphs: [
      "Nothing in these terms limits liability that cannot be limited by law, including for wilful misconduct or gross negligence, or for death or personal injury. Subject to that, we are not liable for lost profit, lost bookings, or indirect loss, and our total liability to you for any claim is limited to the fees you paid us in the 12 months before the event. We are not liable for the tattooing service, its result, or a client’s health.",
    ],
  },
  {
    heading: "18. Governing law and disputes",
    paragraphs: [
      "These terms are governed by Albanian law. Before going to court, each side will try in good faith to settle a dispute within 30 days of written notice. If it is not settled, the courts of Tirana (Gjykata e Rrethit Gjyqësor Tiranë) have jurisdiction. This clause applies to studios, including studios outside Albania. It does not touch any mandatory rights of consumers, who are not parties to this contract.",
    ],
  },
  {
    heading: "19. Ending the contract",
    paragraphs: [
      "You may close the account at any time (refunds follow clause 8 or 9). We may end it if you seriously or repeatedly break these terms, with 14 days’ written notice where the breach can be fixed, or at once for illegal use or a risk to others. After the account ends you can export your data for 30 days. After that we delete it, except what the law or an open legal claim requires us to keep. Clauses that by nature continue (liability, data, disputes) survive.",
    ],
  },
  {
    heading: "20. Changes to these terms and general",
    paragraphs: [
      "We may change these terms for a good reason (a change in law, the service or security). We tell you by email and in the app at least 30 days before the change takes effect. If you do not accept it, you may cancel before then and we refund under clause 8 or 9 as if you had cancelled on the change date.",
      "We may assign this contract to a successor business, with 30 days’ notice to you. Your plan, price and data continue unchanged. These terms, the pricing page and the Data Processing Terms are the whole agreement. If one part is invalid, the rest stands.",
      `Contact: ${LEGAL_CONTACT_EMAIL}, ${LEGAL_ENTITY_ADDRESS}.`,
    ],
  },
];

export const DATA_PROCESSING_CLAUSES: ReadonlyArray<LegalClause> = [
  {
    heading: "Data Processing Terms (processor terms for client data)",
    bullets: [
      "1. Subject and duration. We process client personal data for the studio to provide the service, for as long as the account is active plus the return period in clause 19.",
      "2. Instructions. Only on the studio’s documented instructions, which are these terms and the studio’s use of the settings. We tell you if we think an instruction breaks the law.",
      "3. Confidentiality. Everyone with access is bound by confidentiality.",
      "4. Security. Tenant isolation, encrypted connections, access control, structured logs without personal data, backup and restore, and regular testing.",
      "5. Erasure. When the studio instructs us, or confirms a client’s request, we erase that client’s data within 10 days, and in any case in time for the studio to meet the 30-day legal deadline. Erased data may stay in encrypted database backups for up to 7 days.",
      "6. Sub-processors. The studio gives general authorisation for these: Cloudflare (file and image storage), Resend (email), Twilio (SMS), POK (payments), Hetzner (application hosting, Germany), DigitalOcean (managed database, Frankfurt) and Meta (optional Instagram connection). We give 30 days’ notice of changes and the studio may object. Each sub-processor is bound by equivalent terms.",
      "7. Transfers. Outside Albania and the EEA only on a lawful basis: an adequacy recognition where available, otherwise standard contractual clauses.",
      "8. Help. We help the studio answer client requests and carry out security and impact assessments.",
      "9. Breach. We tell the studio without undue delay, and in any case within 48 hours of confirming it, of a personal data breach affecting its data.",
      "10. End. On ending, we return or delete the data as in clause 19.",
      "11. Audits. We give the studio the information needed to show compliance and allow reasonable audits with notice.",
    ],
  },
];
