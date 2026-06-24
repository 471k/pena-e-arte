import { Check, Copy, Loader2, RefreshCw } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery } from "../studiosApi";
import {
  useGenerateReferralCodeMutation,
  useGetReferralCodeQuery,
  useGetReferralStatsQuery,
} from "../studiosApi";

export function ReferralCodeCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [copied, setCopied] = useState(false);

  const { data: referralCode, isLoading: codeLoading } = useGetReferralCodeQuery(
    studio?.id ?? "",
    { skip: !studio?.id }
  );
  const { data: stats } = useGetReferralStatsQuery(
    studio?.id ?? "",
    { skip: !studio?.id }
  );
  const [generateCode, { isLoading: generating }] = useGenerateReferralCodeMutation();

  if (!studio) return null;

  async function handleGenerate() {
    try {
      await generateCode(studio!.id).unwrap();
      toast.success("Referral code generated.");
    } catch {
      toast.error("Failed to generate referral code.");
    }
  }

  async function handleCopy() {
    if (!referralCode) return;
    await navigator.clipboard.writeText(referralCode.shareUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Referral programme</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Share your referral link. New studios that sign up with your code get one
          month free when they subscribe.
        </p>

        {codeLoading ? (
          <div className="flex items-center gap-2 text-muted-foreground text-sm">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading…
          </div>
        ) : referralCode ? (
          <div className="space-y-3">
            <div className="flex items-center gap-2 rounded-md border bg-muted/40 px-3 py-2">
              <span className="font-mono text-sm flex-1 truncate">{referralCode.shareUrl}</span>
              <Button variant="ghost" size="icon" className="h-7 w-7 shrink-0" onClick={handleCopy}>
                {copied ? <Check className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
              </Button>
            </div>

            <div className="flex items-center justify-between text-xs text-muted-foreground">
              <span>Code: <span className="font-mono font-medium text-foreground">{referralCode.code}</span></span>
              <Button
                variant="ghost"
                size="sm"
                className="h-6 gap-1 text-xs px-2"
                onClick={handleGenerate}
                disabled={generating}
              >
                {generating
                  ? <Loader2 className="h-3 w-3 animate-spin" />
                  : <RefreshCw className="h-3 w-3" />}
                New code
              </Button>
            </div>
          </div>
        ) : (
          <Button
            variant="outline"
            size="sm"
            onClick={handleGenerate}
            disabled={generating}
            className="gap-2"
          >
            {generating && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Generate referral code
          </Button>
        )}

        {stats && (stats.redemptionCount > 0 || referralCode) && (
          <div className="grid grid-cols-2 gap-3 pt-1">
            <div className="rounded-md border px-3 py-2 text-center">
              <p className="text-xl font-semibold">{stats.redemptionCount}</p>
              <p className="text-xs text-muted-foreground">
                Studio{stats.redemptionCount !== 1 ? "s" : ""} referred
              </p>
            </div>
            <div className="rounded-md border px-3 py-2 text-center">
              <p className="text-xl font-semibold">{stats.discountsApplied}</p>
              <p className="text-xs text-muted-foreground">Discounts applied</p>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
