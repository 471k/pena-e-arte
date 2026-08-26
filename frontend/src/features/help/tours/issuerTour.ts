import type { TourStep } from "@/shared/components/OnboardingTour";

export const issuerTourSteps: TourStep[] = [
  {
    targetSelector: '[data-tour="issuer-dashboard-nav"]',
    title: "Platform dashboard",
    body: "How the whole platform is doing — studio counts, revenue, and which studios need attention.",
  },
  {
    targetSelector: '[data-tour="issuer-traffic-nav"]',
    title: "Live traffic",
    body: "See who's on the site right now — guests and signed-in users by role, where they're browsing from, and trends over time.",
  },
  {
    targetSelector: '[data-tour="issuer-studios-nav"]',
    title: "All studios",
    body: "Search, filter, and manage every studio on the platform here.",
  },
  {
    targetSelector: '[data-tour="issuer-plans-nav"]',
    title: "Subscription plans",
    body: "Manage the plan catalogue studios can subscribe to.",
  },
  {
    targetSelector: '[data-tour="issuer-subscriptions-nav"]',
    title: "Subscription oversight",
    body: "Review every studio's subscription status and take action — extend a trial, activate, or cancel.",
  },
  {
    targetSelector: '[data-tour="issuer-audit-log-nav"]',
    title: "Audit log",
    body: "Every suspend, cancel, plan-edit, and other trust-sensitive action taken across the platform, in one searchable log.",
  },
  {
    targetSelector: '[data-tour="issuer-conduct-reports-nav"]',
    title: "Conduct reports",
    body: "Every trust & safety report filed by a client, across every studio — you can resolve any of them, regardless of severity.",
  },
  {
    targetSelector: '[data-tour="issuer-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  },
];
