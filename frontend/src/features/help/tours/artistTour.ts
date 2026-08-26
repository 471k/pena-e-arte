import type { TourStep } from "@/shared/components/OnboardingTour";

export const artistTourSteps: TourStep[] = [
  {
    targetSelector: '[data-tour="artist-schedule-nav"]',
    title: "Your schedule",
    body: "See your appointments for the week here, organized by day.",
  },
  {
    targetSelector: '[data-tour="artist-clients-nav"]',
    title: "Your clients",
    body: "Find any client's profile, tattoo history, and forms here.",
  },
  {
    targetSelector: '[data-tour="artist-create-design-button"]',
    title: "Upload a design",
    body: "Start a new design project here to upload artwork for a client to review.",
    route: "/designs",
  },
  {
    targetSelector: '[data-tour="artist-notifications-bell"]',
    title: "Notifications",
    body: "New bookings, form submissions, and other alerts show up here.",
  },
  {
    targetSelector: '[data-tour="artist-conduct-reports-nav"]',
    title: "Reports about you",
    body: "If a client ever files a conduct report about you, you can read it here — their identity is never shown to you.",
  },
  {
    targetSelector: '[data-tour="artist-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  },
];
