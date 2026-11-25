import { useEffect, useRef, useState } from "react";

interface GoogleSignInButtonProps {
  onCredential: (credential: string) => void;
  disabled?: boolean;
}

/** How long Google gets to size its button before we call it failed. */
const RENDER_CHECK_DELAY_MS = 2500;
/** How often we look for the sized button while waiting. Production renders in ~300 ms. */
const RENDER_POLL_MS = 100;

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
    if (!window.google?.accounts?.id || !container) {
      setStatus("failed");
      return;
    }

    window.google.accounts.id.initialize({
      client_id:             import.meta.env.VITE_GOOGLE_CLIENT_ID as string,
      callback:              ({ credential }) => onCredentialRef.current(credential),
      auto_select:           false,
      cancel_on_tap_outside: true,
    });

    const width = Math.min(container.clientWidth || 300, 400);
    window.google.accounts.id.renderButton(container, {
      type:  "standard",
      theme: "filled_black",
      size:  "large",
      text:  "continue_with",
      shape: "rectangular",
      width,
    });

    const isRendered = (): boolean => {
      const iframe = container.querySelector("iframe");
      return iframe !== null && iframe.clientWidth > 0 && iframe.clientHeight > 0;
    };

    const startedAt = Date.now();
    const pollTimer = window.setInterval(() => {
      if (isRendered()) {
        window.clearInterval(pollTimer);
        setStatus("ready");
      } else if (Date.now() - startedAt >= RENDER_CHECK_DELAY_MS) {
        window.clearInterval(pollTimer);
        setStatus("failed");
      }
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
