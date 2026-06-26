import { useAppSelector } from "@/app/hooks";

export function useSuspensionAwareError(
  isError:        boolean,
  genericMessage: string,
): string | null {
  const isSuspended = useAppSelector((s) => s.ui.studioSuspended);

  if (!isError) return null;

  if (isSuspended) {
    return "Studio access is suspended. Your data is safe — access will be restored once the studio reactivates their subscription.";
  }

  return genericMessage;
}
