import type { LegalClause } from "./legalTypes";
import { LEGAL_ENTITY_NIPT, LEGAL_ENTITY_ADDRESS } from "@/shared/constants/legalEntity";

export const CLIENT_TERMS_CLAUSES: ReadonlyArray<LegalClause> = [
  {
    heading: "1. Who is who",
    paragraphs: [
      `TattooOS is software run by the business registered in Albania under NIPT ${LEGAL_ENTITY_NIPT}, ${LEGAL_ENTITY_ADDRESS}. Studios use it to take bookings, deposits and consent forms. When you book, your agreement for the tattoo is with the studio, not with TattooOS. TattooOS provides the platform and is not responsible for the tattooing service.`,
    ],
  },
  {
    heading: "2. Who may use it",
    paragraphs: [
      "You must be 18 or older to book for yourself. A studio may accept clients aged 14 to 17 only with a parent or guardian who gives consent in person. Nobody under 14 can be tattooed. The studio checks your age and ID in person before tattooing. If your age is not as stated, the studio may refuse the session, and the deposit then follows the studio’s policy.",
    ],
  },
  {
    heading: "3. Your account and bookings",
    paragraphs: [
      "Give true details and keep your login private. You can book as a guest or with an account. An account lets you save drafts, see your history and manage bookings.",
    ],
  },
  {
    heading: "4. Booking a specific date",
    paragraphs: [
      "When you pay a deposit you reserve a particular date and time. Because it is a service for a fixed date, the 14-day right to withdraw from a distance contract does not apply. Instead use the studio’s cancellation policy: if you cancel at least 48 hours before (unless the studio shows a different window), your deposit is refunded in full. See the Refund Policy. Nothing here limits any mandatory consumer right you have under Albanian law or the law of the country you live in.",
    ],
  },
  {
    heading: "5. Deposits and payments",
    paragraphs: [
      "The studio sets the deposit. A card deposit is charged when you pay it and goes to the studio’s own account with our payment partner (POK). You pay exactly the amount shown, with no surcharge from TattooOS. A cash deposit is paid and settled directly with the studio.",
    ],
  },
  {
    heading: "6. Health information and consent",
    paragraphs: [
      "Giving health information is optional, but it helps the artist keep you safe. It goes only to the studio you book with. You can withdraw your consent at any time by asking the studio to delete it. The studio decides whether it can tattoo you safely. A consent form you sign is between you and the studio. Read it before you sign.",
    ],
  },
  {
    heading: "7. What you must not do",
    paragraphs: [
      "Do not give false information or impersonate someone. Do not harass staff or other clients. Do not upload images you do not have the right to use, or unlawful content. Do not try to reach other users’ data or interfere with the service.",
    ],
  },
  {
    heading: "8. Designs and images",
    paragraphs: [
      "Designs, references and photos you upload stay yours. You allow the studio and TattooOS to use them to prepare your booking and design approval. A studio may show finished work in its portfolio only as you and the studio have agreed.",
    ],
  },
  {
    heading: "9. Liability",
    paragraphs: [
      "TattooOS is not liable for the quality, safety or result of a tattoo, or for the studio’s acts. TattooOS is liable under law for its own platform errors, and nothing here excludes liability that cannot be excluded, including for death or personal injury. Disputes about a tattoo, a deposit or a studio’s conduct are first for you and the studio. You can also contact TattooOS through the Contact page to report a platform problem or conduct concern.",
    ],
  },
  {
    heading: "10. Your data",
    paragraphs: [
      "The studio you book with controls your booking, consent and health data. TattooOS processes it for the studio. See the Privacy Policy for your rights.",
    ],
  },
  {
    heading: "11. Changes",
    paragraphs: [
      "We may update these terms. We tell account holders by email and in the app 30 days before a material change. The terms in force when you book apply to that booking.",
    ],
  },
  {
    heading: "12. Law and complaints",
    paragraphs: [
      "Albanian law applies. This does not remove the protection of the mandatory consumer rules of the country you live in, and you may bring a claim before the competent court there. You may also complain to Albania’s consumer protection authorities and, for privacy, to the Information and Data Protection Commissioner.",
    ],
  },
];
