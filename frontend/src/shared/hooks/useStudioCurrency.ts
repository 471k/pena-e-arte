import { useGetMyStudioQuery } from "@/features/studios/studiosApi";

/**
 * The calling owner/artist/client's own studio currency, for any authenticated-owner-side
 * component that needs to format or validate a money value. Public pages (guest booking, public
 * portfolio, gift-card purchase by slug) have no ambient tenant scope — they take the currency
 * from PublicStudioResponse props instead of this hook.
 */
export function useStudioCurrency(): { currency: string | undefined; isLoading: boolean } {
  const { data, isLoading } = useGetMyStudioQuery();
  return { currency: data?.currency, isLoading };
}
