import { useEffect, useRef, useState } from "react";

interface GoogleSignInButtonProps {
  onCredential: (credential: string) => void;
  disabled?: boolean;
}

type GoogleAccountsId = NonNullable<Window["google"]>["accounts"]["id"];

/** How long Google gets to size its button before we call it failed. */
const RENDER_CHECK_DELAY_MS = 2500;
/** How often we look for the SDK and then the sized button while waiting. Production renders in ~300 ms. */
const RENDER_POLL_MS = 100;
/** How long Google's async script gets to arrive before we call it failed (a blocker, or no network to Google). */
const SDK_WAIT_MS = 8000;

/**
 * Renders Google's own "Sign in with Google" button.
 * Required for the built-in popup fallback: Google's button tries FedCM/One Tap
 * first, then opens a real login popup when the browser has no active Google
 * session. A custom-styled button driven by google.accounts.id.prompt() alone
 * cannot do this — prompt() silently no-ops if there's no existing session.
 *
 * renderButton() has no success/failure callback — if the button iframe fails
 * (e.g. a misconfigured or not-yet-propagated OAuth client origin), it leaves
 * the container empty with no visible sign that anything went wrong. We poll
 * for a rendered iframe and surface a fallback message if none appears in time.
 *
 * Until Google has sized its iframe, our container is `inert` and invisible, so it
 * holds no Tab stop (release gate, 25 Nov). In Google's refusal state the iframe is
 * 0x0 but still tabbable; before this, keyboard users met an invisible stop for up
 * to 2.5 s. `invisible` (not `hidden`) keeps the layout, so the width we pass to
 * renderButton() is real and the page doesn't jump when the button appears. We
 * only change our own container; Google's injected markup is never touched.
 */
export function GoogleSignInButton({ onCredential, disabled = false }: GoogleSignInButtonProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");

  // Keep the latest callback in a ref rather than the effect's dependency array — callers
  // rarely memoize inline handlers, and re-running the effect on every new function identity
  // calls google.accounts.id.initialize() again, which logs "called multiple times ... only
  // the last initialized instance will be used" and can drop in-flight sign-in attempts.
  const onCredentialRef = useRef(onCredential);
  useEffect(() => {
    onCredentialRef.current = onCredential;
  }, [onCredential]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      setStatus("failed");
      return;
    }

    const isRendered = (): boolean => {
      const iframe = container.querySelector("iframe");
      return iframe !== null && iframe.clientWidth > 0 && iframe.clientHeight > 0;
    };

    // Google's script is loaded `async` (index.html), so it can arrive AFTER this component mounts. Measured on
    // production 25 Nov: in 4 of 5 loads the component mounted 17-25 ms before window.google existed, and the old
    // "SDK missing at mount means failed" check turned Google sign-in off for good. So we wait for the SDK
    // (SDK_WAIT_MS), then give Google RENDER_CHECK_DELAY_MS to size its button. The container stays inert and
    // invisible throughout, so waiting adds no Tab stop.
    const mountedAt = Date.now();
    let renderedAt: number | null = null;

    const startRender = (id: GoogleAccountsId): void => {
      id.initialize({
        client_id:             import.meta.env.VITE_GOOGLE_CLIENT_ID as string,
        callback:              ({ credential }) => onCredentialRef.current(credential),
        auto_select:           false,
        cancel_on_tap_outside: true,
      });

      const width = Math.min(container.clientWidth || 300, 400);
      id.renderButton(container, {
        type:  "standard",
        theme: "filled_black",
        size:  "large",
        text:  "continue_with",
        shape: "rectangular",
        width,
      });
      renderedAt = Date.now();
    };

    // Returns true when polling should stop.
    const tick = (): boolean => {
      if (renderedAt === null) {
        const id = window.google?.accounts?.id;
        if (id) {
          startRender(id);
        } else if (Date.now() - mountedAt >= SDK_WAIT_MS) {
          setStatus("failed");
          return true;
        }
        return false;
      }
      if (isRendered()) {
        setStatus("ready");
        return true;
      }
      if (Date.now() - renderedAt >= RENDER_CHECK_DELAY_MS) {
        setStatus("failed");
        return true;
      }
      return false;
    };

    if (tick()) return;
    const pollTimer = window.setInterval(() => {
      if (tick()) window.clearInterval(pollTimer);
    }, RENDER_POLL_MS);

    return () => window.clearInterval(pollTimer);
  }, []);

  return (
    <div>
      <div
        ref={containerRef}
        data-testid="google-signin-button"
        data-status={status}
        inert={status !== "ready"}
        className={[
          "flex w-full justify-center",
          disabled ? "pointer-events-none opacity-50" : "",
          status === "loading" ? "invisible" : "",
          status === "failed" ? "hidden" : "",
        ].filter(Boolean).join(" ")}
      />
      {status === "failed" && (
        <p className="text-xs text-destructive-text text-center" role="alert">
          Google Sign-In is unavailable right now. Please try again later or sign in with email.
        </p>
      )}
    </div>
  );
}
