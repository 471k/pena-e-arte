import { useState } from "react";
import { Loader2, Share2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import {
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
} from "@/features/platform/platformApi";
import type { PlatformReferralCodeResponse } from "@/features/platform/platform.types";

interface ReferralCodeRowProps {
  code: PlatformReferralCodeResponse;
}

function ReferralCodeRow({ code }: ReferralCodeRowProps) {
  const [confirm, setConfirm] = useState(false);
  const [deactivate, { isLoading }] = useDeactivateReferralCodeMutation();

  async function handleDeactivate() {
    await deactivate(code.id).unwrap();
    setConfirm(false);
  }

  return (
    <Card>
      <CardContent className="p-4 flex items-start justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-mono font-medium text-sm">{code.code}</span>
            <span
              className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${
                code.isActive
                  ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300"
                  : "bg-muted text-muted-foreground"
              }`}
            >
              {code.isActive ? "active" : "inactive"}
            </span>
            {code.isSingleUse && (
              <span className="text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground">
                single-use
              </span>
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            {code.studioName}
            {" · "}
            {code.redemptionCount} {code.redemptionCount === 1 ? "redemption" : "redemptions"}
            {" · "}
            Created {new Date(code.createdAt).toLocaleDateString("en-GB")}
            {code.expiresAt && ` · Expires ${new Date(code.expiresAt).toLocaleDateString("en-GB")}`}
          </p>
        </div>

        {code.isActive && (
          <div className="flex items-center gap-1.5 shrink-0">
            {confirm ? (
              <>
                <span className="text-xs text-muted-foreground">Deactivate?</span>
                <Button
                  size="sm"
                  variant="destructive"
                  className="h-7 px-2 text-xs"
                  disabled={isLoading}
                  onClick={handleDeactivate}
                >
                  {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes"}
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 px-2 text-xs"
                  onClick={() => setConfirm(false)}
                >
                  No
                </Button>
              </>
            ) : (
              <Button
                size="sm"
                variant="ghost"
                className="h-7 text-xs text-muted-foreground"
                onClick={() => setConfirm(true)}
              >
                Deactivate
              </Button>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function PlatformReferralPage() {
  const { data: codes, isLoading, isError } = useGetPlatformReferralCodesQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Share2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Referral Codes</span>
        {codes && (
          <span className="text-xs text-muted-foreground ml-1">({codes.length})</span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">Failed to load referral codes.</p>
        )}

        {!isLoading && !isError && codes?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No referral codes found.</p>
        )}

        {!isLoading && !isError && codes?.map((code) => (
          <ReferralCodeRow key={code.id} code={code} />
        ))}
      </main>
    </div>
  );
}
