import type { TourStep } from "@/shared/components/OnboardingTour";

export const adminTourSteps: TourStep[] = [
  {
    targetSelector: '[data-tour="admin-dashboard-nav"]',
    title: "Platform dashboard",
    body: "How the whole platform is doing — studio counts, revenue, and which studios need attention.",
  },
  {
    targetSelector: '[data-tour="admin-traffic-nav"]',
    title: "Live traffic",
    body: "See who's on the site right now — guests and signed-in users by role, where they're browsing from, and trends over time.",
  },
  {
    targetSelector: '[data-tour="admin-studios-nav"]',
    title: "All studios",
    body: "Search, filter, and manage every studio on the platform here.",
  },
  {
    targetSelector: '[data-tour="admin-plans-nav"]',
    title: "Subscription plans",
    body: "Manage the plan catalogue studios can subscribe to.",
  },
  {
    targetSelector: '[data-tour="admin-subscriptions-nav"]',
    title: "Subscription oversight",
    body: "Review every studio's subscription status and take action — extend a trial, activate, or cancel.",
  },
  {
    targetSelector: '[data-tour="admin-audit-log-nav"]',
    title: "Audit log",
    body: "Every suspend, cancel, plan-edit, and other trust-sensitive action taken across the platform, in one searchable log.",
  },
  {
    targetSelector: '[data-tour="admin-conduct-reports-nav"]',
    title: "Conduct reports",
    body: "Every trust & safety report filed by a client, across every studio — you can resolve any of them, regardless of severity.",
  },
  {
    targetSelector: '[data-tour="admin-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  },
];
