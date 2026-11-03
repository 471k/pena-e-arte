import type { TourStep } from "@/shared/components/OnboardingTour";

/** `showPaymentMethods` follows the capabilities response's `savedCardsAvailable`: the "Payment
 * Methods" nav entry (and so this step's target) only exists while saved cards are on (C-9). */
export function getClientTourSteps(hasMultipleStudios: boolean, showPaymentMethods: boolean): TourStep[] {
  const steps: TourStep[] = [
    {
      targetSelector: '[data-tour="client-book-nav"]',
      title: "Book an appointment",
      body: "Request a tattoo appointment here — pick an artist, a date, and how long the session should be, or let the studio choose your artist for you.",
    },
    {
      // On My bookings (where the tour runs), so it resolves whenever the client has a booking and is
      // skipped when they have none yet. The page's own next-step button carries
      // data-tour="client-appointment-next-action" for a page-level tour later.
      targetSelector: '[data-tour="client-appointment-details-link"]',
      route: "/book",
      title: "Everything about one appointment",
      body: "Open a booking's own page to see what to do before it, the studio's address once it's confirmed, and to message the studio about it.",
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
      targetSelector: '[data-tour="client-waitlist-nav"]',
      title: "Your waitlist",
      body: "If a slot you wanted wasn't available, see your waitlist entries here — you'll get an email the moment one opens up.",
    },
    {
      targetSelector: '[data-tour="client-designs-nav"]',
      title: "Your designs",
      body: "Once your artist uploads a design draft, you'll review and approve it here.",
    },
  );

  if (showPaymentMethods) {
    steps.push({
      targetSelector: '[data-tour="client-payment-methods-nav"]',
      title: "Save a payment method",
      body: "Save a card here to pay a deposit in one tap next time, without re-entering your card details.",
    });
  }

  steps.push({
    targetSelector: '[data-tour="client-help-button"]',
    title: "Need help?",
    body: "Open this any time for searchable guides and FAQ — or press Shift+? from anywhere.",
  });

  return steps;
}
