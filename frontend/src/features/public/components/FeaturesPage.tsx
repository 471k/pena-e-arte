import { PublicContentLayout } from "./PublicContentLayout";
import { MarketingCtaRow } from "./MarketingCtaRow";

interface FeatureGroup {
  title: string;
  description: string;
}

// Every entry below describes something the product does today (checked against the
// code and the Feature Module Map in docs/claude/architecture.md) — no roadmap copy.
const FEATURE_GROUPS: ReadonlyArray<FeatureGroup> = [
  {
    title: "Online booking & deposits",
    description:
      "Clients book the artist and time they want, with no account needed. Each studio sets its own deposit rules, as a fixed amount or a percentage.",
  },
  {
    title: "Digital consent forms",
    description:
      "Clients sign intake and consent forms before the appointment. Every signature is timestamped and stored with the exact wording the client agreed to.",
  },
  {
    title: "Design approval",
    description:
      "Artists share design revisions and clients approve them or ask for changes, all in one thread tied to the booking.",
  },
  {
    title: "Client profiles & history",
    description:
      "Each client's tattoo history, notes and body map live in one place, instead of paper files and message threads.",
  },
  {
    title: "Automatic reminders",
    description:
      "Appointment reminders go out by email and SMS on their own, so fewer clients forget their session.",
  },
  {
    title: "Studio & artist pages",
    description:
      "Every studio and artist gets a shareable page with a portfolio, reviews and a booking link, plus a booking widget you can embed on your own site.",
  },
];

export function FeaturesPage() {
  return (
    <PublicContentLayout
      title="Features — TattooOS"
      description="Online booking, deposits, digital consent forms, design approvals and client records — everything a tattoo studio needs in one place."
      canonicalPath="/features"
    >
      <h1 className="text-3xl font-semibold tracking-tight">Features</h1>
      <p className="mt-3 text-muted-foreground">
        Everything below is built and working today, not a roadmap.
      </p>

      <div className="mt-8 grid gap-6 sm:grid-cols-2">
        {FEATURE_GROUPS.map((group) => (
          <section key={group.title} className="rounded-lg border p-5">
            <h2 className="font-semibold">{group.title}</h2>
            <p className="mt-2 text-sm text-muted-foreground">{group.description}</p>
          </section>
        ))}
      </div>

      <MarketingCtaRow />
    </PublicContentLayout>
  );
}
