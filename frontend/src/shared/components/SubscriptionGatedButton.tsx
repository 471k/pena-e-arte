import type { ComponentProps } from "react";
import { Button } from "@/shared/components/ui/button";
import { useSubscriptionGuard } from "@/features/billing/useSubscriptionGuard";
import { SUPPORT_VIEW_READ_ONLY_REASON } from "@/shared/hooks/useSupportView";

type Props = ComponentProps<typeof Button>;

// A control that writes. In an admin's support session (D05) it is disabled with the read-only reason, for every role
// the session can show; otherwise the subscription part is owner-only (useSubscriptionGuard reads nothing for other
// roles). Use it for every write control: its default is right, so a page can't forget to set it (D05 phase 4b). The reason used to be a hover-only tooltip
// on a `disabled` button, which a keyboard can't focus and a phone can't hover: the owner saw a dead button.
// It is now the Button's own disabledReason, so focus shows it and a screen reader reads it (slice 6a). The
// tooltip's "Subscribe" link went with it, because a link inside a tooltip can't be reached either; the
// read-only banner and Billing in the menu are where that action lives.
export function SubscriptionGatedButton({ children, ...props }: Props) {
  const { isReadOnly, cause } = useSubscriptionGuard();

  const reason = !isReadOnly
    ? null
    : cause === "support"
      ? SUPPORT_VIEW_READ_ONLY_REASON
      : cause === "suspended"
      ? "The platform has suspended this studio, so changes can't be saved."
      : "The studio's subscription is inactive, so changes can't be saved. Subscribe from Billing to save again.";

  return (
    <Button {...props} disabledReason={reason ?? props.disabledReason}>
      {children}
    </Button>
  );
}
