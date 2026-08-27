import type { TourStep } from "@/shared/components/OnboardingTour";

export function getClientTourSteps(hasMultipleStudios: boolean): TourStep[] {
  const steps: TourStep[] = [
    {
      targetSelector: '[data-tour="client-book-nav"]',
      title: "Book an appointment",
      body: "Request a tattoo appointment here — pick an artist, a date, and how long the session should be, or let the studio choose your artist for you.",
    },
  ];

  if (hasMultipleStudios) {
    steps.push({
      targetSelector: '[data-tour="client-my-studios-nav"]',
      title: "Switch between studios",
      body: "You've booked with more than one studio — switch which one is active here, any time.",
    });
  }

  steps.push(
    {
      targetSelector: '[data-tour="client-messages-nav"]',
      title: "Message your artist or the studio",
      body: "Send a real-time message to your assigned or booked artist, or to the studio owner, right from here.",
    },
    {
      targetSelector: '[data-tour="client-designs-nav"]',
      title: "Your designs",
      body: "Once your artist uploads a design draft, you'll review and approve it here.",
    },
    {
      targetSelector: '[data-tour="client-help-button"]',
      title: "Need help?",
      body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
    },
  );

  return steps;
}
