import { BadgeCheck } from "lucide-react";

interface VerifiedSocialBadgeProps {
  platform: string;
  className?: string;
}

/**
 * Same badge variant/color language as ReviewSection.tsx's "Verified client" badge —
 * a checkmark that means something backed by real data, not a new visual system.
 */
export function VerifiedSocialBadge({ platform, className }: VerifiedSocialBadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-0.5
                  text-[10px] font-medium text-violet-400
                  px-1.5 py-0.5 rounded-full
                  bg-violet-500/10 border border-violet-500/20 ${className ?? ""}`}
      title={`We've directly confirmed this ${platform} account belongs to them.`}
    >
      <BadgeCheck className="h-2.5 w-2.5" aria-hidden="true" />
      Verified
    </span>
  );
}
