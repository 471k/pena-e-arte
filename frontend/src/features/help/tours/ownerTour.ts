import type { TourStep } from "@/shared/components/OnboardingTour";

export const ownerTourSteps: TourStep[] = [
  {
    targetSelector: '[data-tour="owner-dashboard-nav"]',
    title: "Your dashboard",
    body: "A snapshot of today's and this week's appointments, and any deposits still owed.",
  },
  {
    targetSelector: '[data-tour="owner-add-artist-nav"]',
    title: "Add your artists",
    body: "Manage your studio's artists here, or add a new one.",
  },
  {
    targetSelector: '[data-tour="owner-messages-nav"]',
    title: "Message any artist or client",
    body: "Send a real-time message to any artist or client at your studio, right from here.",
  },
  {
    targetSelector: '[data-tour="owner-become-artist-cta"]',
    title: "Also work as an artist?",
    body: "If you tattoo yourself, enable your own artist profile here — no second account needed.",
    route: "/artists",
  },
  {
    targetSelector: '[data-tour="owner-solo-publish-banner"]',
    title: "Get discoverable",
    body: "Add a real city and location in Studio Settings to appear on the Studio Map and in Discover. Only shown for a solo studio that hasn't published yet.",
    route: "/artists",
  },
  {
    targetSelector: '[data-tour="owner-join-invite-bell"]',
    title: "Studio join invites",
    body: "If another studio invites you to join them as an artist, it shows up here. Only appears when you have a pending invite.",
  },
  {
    targetSelector: '[data-tour="owner-deposit-rules-nav"]',
    title: "Set up deposit rules",
    body: "Deposit rules decide how much clients pay upfront to secure a booking.",
  },
  {
    targetSelector: '[data-tour="owner-studio-profile-nav"]',
    title: "Your studio profile",
    body: "Edit your studio's public details, branding, booking widget, QR code, and referral code here.",
  },
  {
    targetSelector: '[data-tour="owner-billing-nav"]',
    title: "Billing & subscription",
    body: "Check your trial status, plan usage, and manage your subscription here.",
  },
  {
    targetSelector: '[data-tour="owner-reports-nav"]',
    title: "Revenue reports",
    body: "See your monthly revenue trend and a breakdown by artist here.",
  },
  {
    targetSelector: '[data-tour="owner-conduct-reports-nav"]',
    title: "Conduct reports",
    body: "If a client ever reports a serious issue with your studio or an artist, it lands here.",
  },
  {
    targetSelector: '[data-tour="owner-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  },
];
