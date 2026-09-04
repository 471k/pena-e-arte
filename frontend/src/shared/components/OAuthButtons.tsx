import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { GoogleSignInButton } from "@/shared/components/GoogleSignInButton";
import { useAppleSignIn }     from "@/shared/hooks/useAppleSignIn";

interface OAuthButtonsProps {
  onToken: (result: { provider: "google" | "apple"; idToken: string }) => Promise<void>;
  disabled?: boolean;
}

export function OAuthButtons({ onToken, disabled = false }: OAuthButtonsProps) {
  const [loadingProvider, setLoadingProvider] = useState<"google" | "apple" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signInWithApple = useAppleSignIn();
  // Unlike GoogleSignInButton, which detects a render failure itself and shows a
  // graceful fallback, this button has no such check — it would otherwise render
  // normally and only fail when clicked. Gate it on a real client ID existing so an
  // unconfigured Apple OAuth app (no credentials provisioned yet) means an absent
  // button, not a visibly broken one.
  const isAppleConfigured = Boolean(import.meta.env.VITE_APPLE_CLIENT_ID);

  async function handleGoogleCredential(idToken: string) {
    setError(null);
    setLoadingProvider("google");
    try {
      await onToken({ provider: "google", idToken });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Sign-in failed. Please try again.";
      setError(msg);
    } finally {
      setLoadingProvider(null);
    }
  }

  async function handleApple() {
    setError(null);
    setLoadingProvider("apple");
    try {
      const idToken = await signInWithApple();
      await onToken({ provider: "apple", idToken });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Sign-in failed. Please try again.";
      // User-cancelled prompts produce an error we don't surface as an error to the user.
      if (!msg.toLowerCase().includes("dismiss") && !msg.toLowerCase().includes("cancel")) {
        setError(msg);
      }
    } finally {
      setLoadingProvider(null);
    }
  }

  const isLoading = loadingProvider !== null;

  return (
    <div className="space-y-3">
      {/* Divider */}
      <div className="relative">
        <div className="absolute inset-0 flex items-center">
          <span className="w-full border-t border-border/50" />
        </div>
        <div className="relative flex justify-center text-xs">
          <span className="bg-card px-2 text-foreground/40">or continue with</span>
        </div>
      </div>

      {/* Google */}
      <div className="relative">
        <GoogleSignInButton onCredential={handleGoogleCredential} disabled={disabled || isLoading} />
        {loadingProvider === "google" && (
          <div className="absolute inset-0 flex items-center justify-center rounded-md bg-card/90">
            <Loader2 className="h-4 w-4 animate-spin" />
          </div>
        )}
      </div>

      {/* Apple — only when a real client ID is configured, see isAppleConfigured above */}
      {isAppleConfigured && (
        <Button
          type="button"
          variant="outline"
          className="w-full gap-2"
          disabled={disabled || isLoading}
          onClick={handleApple}
          aria-label="Continue with Apple"
        >
          {loadingProvider === "apple"
            ? <Loader2 className="h-4 w-4 animate-spin" />
            : (
              <svg role="img" aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor">
                <path d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 3.014-2.117 3.675-.546 9.103 1.519 12.09 1.013 1.454 2.208 3.09 3.792 3.039 1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857-.026-3.04 2.48-4.494 2.597-4.559-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052-2.662.805-3.532 1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701"/>
              </svg>
            )
          }
          Continue with Apple
        </Button>
      )}

      {error && (
        <p className="text-xs text-destructive text-center" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
