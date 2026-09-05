import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { SocialLinksCard } from "@/features/social/components/SocialLinksCard";
import { useGetMyStudioQuery } from "../studiosApi";

/**
 * Self-contained wrapper matching this page's other sibling cards (ReferralCodeCard,
 * BrandingSettingsCard, ...) — fetches its own "my studio" id rather than requiring
 * the parent StudioProfilePage to thread it through.
 */
export function StudioSocialLinksCard() {
  const { data: studio, isLoading } = useGetMyStudioQuery();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Social Media</CardTitle>
      </CardHeader>
      <CardContent className="space-y-1">
        <p className="text-xs text-muted-foreground mb-3">
          Connect or verify your studio's accounts. A green check means we've directly
          confirmed the account belongs to you — clients see this on your public page.
        </p>
        {isLoading || !studio ? (
          <Skeleton className="h-14 w-full" />
        ) : (
          <SocialLinksCard subjectType="Studio" subjectId={studio.id} />
        )}
      </CardContent>
    </Card>
  );
}
