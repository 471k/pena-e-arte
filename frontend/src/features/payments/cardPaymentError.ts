// POK's SDK passes its own API error text to onError: "3DS processing failed. Please try again" (the
// same text for a failed AND a rejected 3-D Secure), "Your card was not accepted. Please change the
// details and try again!" (number refused), or just "Something went wrong!" (e.g. 3-D Secure
// cancelled). That's technical and doesn't tell the client what to do next, so map it to a plain
// next step. Messages as observed on POK staging, October 2026.
export function describeCardPaymentError(message?: string | null): string {
  const text = message ?? "";
  if (/3ds|3-d secure|authenticat/i.test(text)) {
    return "Your bank couldn't verify this card. Try again, or use a different card.";
  }
  if (/not accepted|invalid|change the details/i.test(text)) {
    return "This card wasn't accepted. Check the card details, or use a different card.";
  }
  return "The card payment didn't go through. Try again, or use a different card.";
}
