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
    targetSelector: '[data-tour="owner-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  },
];
