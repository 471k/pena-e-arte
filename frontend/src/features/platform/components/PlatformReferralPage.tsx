import { useState } from "react";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  Check,
  ClipboardCopy,
  Loader2,
  Plus,
  Share2,
  Trash2,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Label } from "@/shared/components/ui/label";
import { useGetStudiosQuery } from "@/features/studios/studiosApi";
import {
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
  useReactivateReferralCodeMutation,
  useDeleteReferralCodeMutation,
  useGenerateReferralCodeForStudioMutation,
} from "@/features/platform/platformApi";
import type { PlatformReferralCodeResponse } from "@/features/platform/platform.types";

function fmt(date: string | Date): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function ReferralCodeRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-5 w-14 rounded-full" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-56" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-7" />
            <Skeleton className="h-7 w-20" />
            <Skeleton className="h-7 w-14" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

interface ReferralCodeRowProps {
  code: PlatformReferralCodeResponse;
}

export function ReferralCodeRow({ code }: ReferralCodeRowProps) {
  const [copied,       setCopied]       = useState(false);
  const [deactivating, setDeactivating] = useState(false);
  const [reactivating, setReactivating] = useState(false);
  const [deleting,     setDeleting]     = useState(false);

  const [deactivate, { isLoading: deactivating_ }] = useDeactivateReferralCodeMutation();
  const [reactivate, { isLoading: reactivating_ }] = useReactivateReferralCodeMutation();
  const [deleteFn,   { isLoading: deleting_     }] = useDeleteReferralCodeMutation();

  async function handleCopy() {
    await navigator.clipboard.writeText(code.code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  async function handleDeactivate() {
    try {
      await deactivate(code.id).unwrap();
      toast.success("Code deactivated");
      setDeactivating(false);
    } catch {
      toast.error("Failed to deactivate code");
    }
  }

  async function handleReactivate() {
    try {
      await reactivate(code.id).unwrap();
      toast.success("Code reactivated");
      setReactivating(false);
    } catch {
      toast.error("Failed to reactivate code");
    }
  }

  async function handleDelete() {
    try {
      await deleteFn(code.id).unwrap();
      toast.success("Code deleted");
    } catch {
      toast.error("Failed to delete code");
    }
  }

  const anyExpanded = deactivating || reactivating || deleting;

  const statusClass = code.isActive
    ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300"
    : "bg-muted text-muted-foreground";

  return (
    <Card>
      <CardContent className="p-4 space-y-2">

        {/* ── Main row ─────────────────────────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-nowrap min-w-0">
              <span className="font-mono font-medium text-sm shrink-0">{code.code}</span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${statusClass}`}>
                {code.isActive ? "Active" : "Inactive"}
              </span>
              {code.isSingleUse && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground shrink-0">
                  Single use
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {code.studioName}
              {" · "}
              {code.redemptionCount} {code.redemptionCount === 1 ? "redemption" : "redemptions"}
              {" · "}
              Generated {fmt(code.createdAt)}
              {code.expiresAt && ` · Expires ${fmt(code.expiresAt)}`}
            </p>
          </div>

          {/* ── Action zone ──────────────────────────────────────── */}
          {!anyExpanded && (
            <div className="flex items-center gap-1.5 shrink-0">

              <Button
                size="sm"
                variant="ghost"
                className="h-7 w-7 p-0"
                onClick={handleCopy}
                aria-label={`Copy referral code ${code.code}`}
                title={copied ? "Copied!" : "Copy code"}
              >
                {copied
                  ? <Check className="h-3.5 w-3.5 text-green-500" />
                  : <ClipboardCopy className="h-3.5 w-3.5" />}
              </Button>

              {code.isActive ? (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 text-xs text-muted-foreground"
                  onClick={() => setDeactivating(true)}
                  aria-label={`Deactivate referral code ${code.code}`}
                >
                  Deactivate
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 text-xs"
                  onClick={() => setReactivating(true)}
                  aria-label={`Reactivate referral code ${code.code}`}
                >
                  Reactivate
                </Button>
              )}

              <Button
                size="sm"
                variant="ghost"
                className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive transition-colors"
                onClick={() => setDeleting(true)}
                aria-label={`Delete referral code ${code.code}`}
                title="Delete"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>

            </div>
          )}
        </div>

        {/* ── Deactivate confirmation ──────────────────────────────── */}
        {deactivating && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">
              Deactivate code <strong className="font-mono">{code.code}</strong>?
            </span>
            <Button
              size="sm"
              variant="destructive"
              className="h-7 px-2 text-xs"
              disabled={deactivating_}
              onClick={handleDeactivate}
            >
              {deactivating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, deactivate"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setDeactivating(false)}
            >
              Cancel
            </Button>
          </div>
        )}

        {/* ── Reactivate confirmation ──────────────────────────────── */}
        {reactivating && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">
              Reactivate code <strong className="font-mono">{code.code}</strong>?
              {" "}Any other active code for this studio will be deactivated.
            </span>
            <Button
              size="sm"
              className="h-7 px-2 text-xs"
              disabled={reactivating_}
              onClick={handleReactivate}
            >
              {reactivating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, reactivate"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setReactivating(false)}
            >
              Cancel
            </Button>
          </div>
        )}

        {/* ── Delete confirmation ──────────────────────────────────── */}
        {deleting && (
          <div className="pt-2 space-y-1 border-t">
            {code.redemptionCount > 0 ? (
              <p className="text-xs text-amber-600 dark:text-amber-400">
                This code has {code.redemptionCount}{" "}
                {code.redemptionCount === 1 ? "redemption" : "redemptions"} —
                it cannot be deleted. Deactivate it instead.
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                Permanently delete code <strong className="font-mono">{code.code}</strong>?
                This cannot be undone.
              </p>
            )}
            <div className="flex items-center gap-2">
              {code.redemptionCount === 0 && (
                <Button
                  size="sm"
                  variant="destructive"
                  className="h-7 px-2 text-xs"
                  disabled={deleting_}
                  onClick={handleDelete}
                >
                  {deleting_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
                </Button>
              )}
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setDeleting(false)}
              >
                Cancel
              </Button>
            </div>
          </div>
        )}

      </CardContent>
    </Card>
  );
}

interface GenerateFormProps {
  onClose: () => void;
}

function GenerateCodeForm({ onClose }: GenerateFormProps) {
  const [studioId,  setStudioId]  = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const { data: studios = [] } = useGetStudiosQuery();
  const [generate, { isLoading }] = useGenerateReferralCodeForStudioMutation();

  async function handleGenerate() {
    if (!studioId) return;
    try {
      await generate({ studioId, expiresAt: expiresAt || undefined }).unwrap();
      toast.success("Referral code generated");
      onClose();
    } catch {
      toast.error("Failed to generate code");
    }
  }

  const minDate = new Date();
  minDate.setDate(minDate.getDate() + 1);
  const minDateStr = minDate.toISOString().split("T")[0];

  return (
    <Card className="mb-4">
      <CardContent className="p-4 space-y-3">
        <p className="text-xs font-medium">Generate Referral Code</p>
        <div className="space-y-1">
          <Label htmlFor="gen-studio" className="text-xs">Studio</Label>
          <select
            id="gen-studio"
            value={studioId}
            onChange={(e) => setStudioId(e.target.value)}
            className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
          >
            <option value="">Select a studio…</option>
            {studios.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="gen-expires" className="text-xs">
            Expiry date <span className="text-muted-foreground font-normal">(optional)</span>
          </Label>
          <input
            id="gen-expires"
            type="date"
            min={minDateStr}
            value={expiresAt}
            onChange={(e) => setExpiresAt(e.target.value)}
            className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
          />
        </div>
        <p className="text-xs text-muted-foreground">
          Generates an 8-character single-use code. Any existing active code
          for the selected studio will be deactivated.
        </p>
        <div className="flex gap-2">
          <Button
            size="sm"
            className="h-7 px-3 text-xs gap-1"
            disabled={isLoading || !studioId}
            onClick={handleGenerate}
          >
            {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Generate"}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="h-7 px-3 text-xs"
            onClick={onClose}
          >
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function PlatformReferralPage() {
  useDocumentMeta({ title: "Referral Codes — Platform Admin", canonical: "/platform/referrals" });

  const [generating,   setGenerating]   = useState(false);
  const [search,       setSearch]       = useState("");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");

  const { data: codes, isLoading, isError } = useGetPlatformReferralCodesQuery();

  const q = search.trim().toLowerCase();

  const filtered = (codes ?? []).filter((c) => {
    const matchesSearch = !q ||
      c.code.toLowerCase().includes(q) ||
      c.studioName.toLowerCase().includes(q);
    const matchesStatus =
      statusFilter === "all" ||
      (statusFilter === "active"   &&  c.isActive) ||
      (statusFilter === "inactive" && !c.isActive);
    return matchesSearch && matchesStatus;
  });

  return (
    <div className="min-h-screen bg-background">

      {/* ── Sticky header ───────────────────────────────────────── */}
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <Share2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Referral Codes</span>
        {codes && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {codes.length}
          </span>
        )}
        <Button
          size="sm"
          className="ml-auto h-7 text-xs gap-1"
          onClick={() => setGenerating((g) => !g)}
          aria-label="Generate new referral code"
        >
          <Plus className="h-3.5 w-3.5" />
          Generate Code
        </Button>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">

        <p className="text-xs text-muted-foreground">
          Referral codes give studios a shareable link for recruiting new tenants.
          Each studio can have one active code at a time.
        </p>

        {generating && (
          <GenerateCodeForm onClose={() => setGenerating(false)} />
        )}

        {!isLoading && !isError && codes && codes.length > 0 && (
          <div className="flex gap-2 flex-wrap items-center">
            <div className="relative flex-1 min-w-48">
              <input
                type="search"
                placeholder="Search by code or studio…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="h-8 w-full rounded-md border border-input bg-background
                           px-3 text-xs placeholder:text-muted-foreground
                           focus:outline-none focus:ring-1 focus:ring-ring"
                aria-label="Search referral codes"
              />
            </div>
            <div className="flex gap-1">
              {(["all", "active", "inactive"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => setStatusFilter(s)}
                  className={`text-xs px-2.5 py-1 rounded-full border transition-colors capitalize ${
                    statusFilter === s
                      ? "bg-primary text-primary-foreground border-primary"
                      : "hover:bg-muted border-border"
                  }`}
                >
                  {s === "all"
                    ? `All (${codes.length})`
                    : s === "active"
                      ? `Active (${codes.filter((c) => c.isActive).length})`
                      : `Inactive (${codes.filter((c) => !c.isActive).length})`}
                </button>
              ))}
            </div>
          </div>
        )}

        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <ReferralCodeRowSkeleton key={i} />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load referral codes.
          </p>
        )}

        {!isLoading && !isError && filtered.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <Share2 className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">
              {codes?.length === 0
                ? "No referral codes yet."
                : "No codes match your search."}
            </p>
            {codes?.length === 0 && (
              <Button
                size="sm"
                variant="outline"
                className="gap-1.5 text-xs mt-1"
                onClick={() => setGenerating(true)}
              >
                <Plus className="h-3.5 w-3.5" />
                Generate first code
              </Button>
            )}
            {codes && codes.length > 0 && (
              <Button
                size="sm"
                variant="ghost"
                className="text-xs"
                onClick={() => { setSearch(""); setStatusFilter("all"); }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {!isLoading && !isError && filtered.map((code) => (
          <ReferralCodeRow key={code.id} code={code} />
        ))}

      </main>
    </div>
  );
}
