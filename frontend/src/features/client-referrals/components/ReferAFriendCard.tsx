import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Badge } from "@/shared/components/ui/badge";
import {
  useGetOrCreateMyReferralCodeQuery,
  useGetMyReferralRewardsQuery,
} from "@/features/client-referrals/clientReferralsApi";

/// Two-sided referral: the code shared here rewards the friend who books with it (percent
/// off their deposit) AND earns this client a matching credit, shown below once earned —
/// spent by pre-checking "Apply your referral credit" on this client's own next booking.
export function ReferAFriendCard() {
  const { data: referral, isLoading, isError } = useGetOrCreateMyReferralCodeQuery();
  const { data: rewards } = useGetMyReferralRewardsQuery();
  const unredeemedRewards = rewards?.filter((r) => !r.isRedeemed) ?? [];

  const handleCopy = async () => {
    if (!referral) return;
    try {
      await navigator.clipboard.writeText(referral.shareUrl);
      toast.success("Referral link copied.");
    } catch {
      toast.error("Couldn't copy the link — copy it manually instead.");
    }
  };

  if (isLoading) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Refer a friend</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-9 w-32" />
        </CardContent>
      </Card>
    );
  }

  if (isError || !referral) {
    return null;
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Refer a friend</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Share your code — your friend gets {referral.rewardPercent}% off their deposit when
          they book, and you earn a {referral.rewardPercent}% credit toward your own next
          booking.
        </p>

        <div className="flex flex-wrap items-center gap-2">
          <code className="rounded-md border bg-muted px-3 py-2 text-sm font-mono tracking-wide">
            {referral.code}
          </code>
          <Button type="button" variant="secondary" onClick={handleCopy}>
            Copy share link
          </Button>
        </div>

        <p className="text-xs text-muted-foreground">
          Redeemed {referral.redemptionCount} time{referral.redemptionCount === 1 ? "" : "s"} so
          far.
        </p>

        {unredeemedRewards.length > 0 && (
          <div className="space-y-1 rounded-md border border-dashed p-3">
            <p className="text-sm font-medium">You have referral credit available</p>
            {unredeemedRewards.map((reward) => (
              <Badge key={reward.id} variant="secondary" className="mr-2">
                {reward.rewardPercent}% off your next booking
              </Badge>
            ))}
            <p className="text-xs text-muted-foreground">
              Apply it from the "Apply your referral credit" option when you book.
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
