import { useEffect, useRef, useState } from "react";

interface GoogleSignInButtonProps {
  onCredential: (credential: string) => void;
  disabled?: boolean;
}

const RENDER_CHECK_DELAY_MS = 2500;

/**
 * Renders Google's own "Sign in with Google" button.
 * Required for the built-in popup fallback: Google's button tries FedCM/One Tap
 * first, then opens a real login popup when the browser has no active Google
 * session. A custom-styled button driven by google.accounts.id.prompt() alone
 * cannot do this — prompt() silently no-ops if there's no existing session.
 *
 * renderButton() has no success/failure callback — if the button iframe fails
 * (e.g. a misconfigured or not-yet-propagated OAuth client origin), it leaves
 * the container empty with no visible sign that anything went wrong. We check
 * for a rendered iframe shortly after and surface a fallback message instead
 * of silently showing nothing.
 */
export function GoogleSignInButton({ onCredential, disabled = false }: GoogleSignInButtonProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");

  useEffect(() => {
    const container = containerRef.current;
    if (!window.google?.accounts?.id || !container) {
      setStatus("failed");
      return;
    }

    window.google.accounts.id.initialize({
      client_id:             import.meta.env.VITE_GOOGLE_CLIENT_ID as string,
      callback:              ({ credential }) => onCredential(credential),
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

    const checkTimer = window.setTimeout(() => {
      const iframe = container.querySelector("iframe");
      const rendered = iframe !== null && iframe.clientWidth > 0 && iframe.clientHeight > 0;
      setStatus(rendered ? "ready" : "failed");
    }, RENDER_CHECK_DELAY_MS);

    return () => window.clearTimeout(checkTimer);
  }, [onCredential]);

  return (
    <div>
      <div
        ref={containerRef}
        data-testid="google-signin-button"
        className={[
          "flex w-full justify-center",
          disabled ? "pointer-events-none opacity-50" : "",
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
