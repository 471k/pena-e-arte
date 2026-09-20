import { useId, type ReactNode } from "react";
import { ExternalLink, Unlink } from "lucide-react";
import { Badge } from "@/shared/components/ui/badge";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { VerifiedSocialBadge } from "@/shared/components/VerifiedSocialBadge";
import type { SocialIcon } from "@/shared/utils/socialPlatforms";

/** What the row's single primary action does. Only one button per row, never two. */
export type ConnectionRowAction =
  | { kind: "connect"; onClick: () => void; busy?: boolean }
  | { kind: "get-code"; onClick: () => void; disabled: boolean; busy?: boolean }
  | { kind: "disconnect"; onClick: () => void }
  /** Platform isn't configured on this server: no button, the row says so in text. */
  | { kind: "unavailable" };

export interface ConnectionRowHandleInput {
  value: string;
  onChange: (value: string) => void;
  onBlur: () => void;
  /** Inline validation/save failure shown under the field (in addition to any toast). */
  error?: string | null;
  /** Announced politely to screen readers next to the field. */
  status?: "idle" | "saving" | "saved";
}

export interface ConnectionRowProps {
  icon: SocialIcon;
  label: string;
  /** The linked/typed handle, without the leading "@". */
  handle: string | null;
  isVerified: boolean;
  /** null = read-only viewer: no action slot at all. */
  action: ConnectionRowAction | null;
  /** Editable handle field (managers only, unverified rows). Replaces the static @handle. */
  handleInput?: ConnectionRowHandleInput;
  /** One helper sentence shown when there is no handle yet. */
  helper?: string;
  /** Extra always-visible line under the handle, e.g. "Last synced 3 Jul 2026 · 12 posts". */
  detail?: string;
  /** Content rendered inside the row's <li>, directly under the card (e.g. Instagram's synced posts). */
  children?: ReactNode;
}

function statusOf(
  isVerified: boolean, handle: string | null, action: ConnectionRowAction | null,
): "Verified" | "Handle added" | "Not linked" | "Unavailable" {
  if (isVerified) return "Verified";
  if (action?.kind === "unavailable") return "Unavailable";
  return handle ? "Handle added" : "Not linked";
}

/**
 * One connection row — icon, label, status badge (text, never colour alone), the handle or a reason,
 * and at most one primary action. Used for all five platforms so they read as one pattern.
 */
export function ConnectionRow({
  icon: Icon, label, handle, isVerified, action, handleInput, helper, detail, children,
}: ConnectionRowProps) {
  const errorId = useId();
  const status = statusOf(isVerified, handle, action);
  const busy = action !== null && "busy" in action && action.busy === true;

  return (
    <li className="space-y-3">
      <Card>
        <CardContent className="p-4 flex flex-col sm:flex-row sm:items-center gap-3">
          <div className="flex items-start gap-3 flex-1 min-w-0">
            <Icon className="h-5 w-5 mt-0.5 shrink-0 text-muted-foreground" />

            <div className="min-w-0 flex-1 space-y-1">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-sm font-medium">{label}</span>
                {status === "Verified" ? (
                  <VerifiedSocialBadge platform={label} />
                ) : (
                  <Badge variant="outline" className="text-[11px] font-medium px-2 py-0">{status}</Badge>
                )}
              </div>

              {handleInput ? (
                <div className="space-y-1">
                  <div className="relative max-w-[260px]">
                    <span
                      className="absolute left-3 top-1/2 -translate-y-1/2 text-sm text-muted-foreground pointer-events-none"
                      aria-hidden="true"
                    >
                      @
                    </span>
                    <Input
                      value={handleInput.value}
                      onChange={(e) => handleInput.onChange(e.target.value)}
                      onBlur={handleInput.onBlur}
                      placeholder="handle"
                      className="h-9 text-sm pl-7"
                      aria-label={`${label} handle`}
                      aria-invalid={handleInput.error ? true : undefined}
                      aria-describedby={handleInput.error ? errorId : undefined}
                      autoCapitalize="off"
                      autoCorrect="off"
                      spellCheck={false}
                    />
                  </div>
                  {handleInput.error && (
                    <p id={errorId} className="text-xs text-destructive-text">{handleInput.error}</p>
                  )}
                  <p role="status" aria-live="polite" className="text-xs text-muted-foreground empty:hidden">
                    {handleInput.status === "saving" ? "Saving…" : handleInput.status === "saved" ? "Saved" : ""}
                  </p>
                </div>
              ) : handle ? (
                <p className="text-sm break-all">@{handle}</p>
              ) : busy ? (
                <p className="text-xs text-muted-foreground">Opening {label}…</p>
              ) : helper ? (
                <p className="text-xs text-muted-foreground">{helper}</p>
              ) : null}

              {action?.kind === "unavailable" && (
                <p className="text-xs text-muted-foreground">Not available on this server yet.</p>
              )}

              {detail && <p className="text-xs text-muted-foreground">{detail}</p>}
            </div>
          </div>

          {action?.kind === "connect" && (
            <Button
              size="sm"
              className="min-h-10 w-full sm:w-auto gap-1.5"
              onClick={action.onClick}
              disabled={action.busy}
              aria-busy={action.busy}
              aria-label={`Connect ${label}`}
            >
              Connect
              <ExternalLink className="h-3 w-3" aria-hidden="true" />
            </Button>
          )}
          {action?.kind === "get-code" && (
            <Button
              size="sm"
              variant="outline"
              className="min-h-10 w-full sm:w-auto"
              onClick={action.onClick}
              disabled={action.disabled || action.busy}
              aria-busy={action.busy}
              aria-label={`Get ${label} verification code`}
            >
              Get verification code
            </Button>
          )}
          {action?.kind === "disconnect" && (
            <Button
              size="sm"
              variant="ghost"
              className="min-h-10 w-full sm:w-auto gap-1.5 text-destructive-text hover:text-destructive-text"
              onClick={action.onClick}
              aria-label={`Disconnect ${label}`}
            >
              <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
              Disconnect
            </Button>
          )}
        </CardContent>
      </Card>
      {children}
    </li>
  );
}
