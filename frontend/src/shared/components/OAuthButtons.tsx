import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useGoogleSignIn } from "@/shared/hooks/useGoogleSignIn";
import { useAppleSignIn }  from "@/shared/hooks/useAppleSignIn";

interface OAuthButtonsProps {
  onToken: (result: { provider: "google" | "apple"; idToken: string }) => Promise<void>;
  disabled?: boolean;
}

export function OAuthButtons({ onToken, disabled = false }: OAuthButtonsProps) {
  const [loadingProvider, setLoadingProvider] = useState<"google" | "apple" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signInWithGoogle = useGoogleSignIn();
  const signInWithApple  = useAppleSignIn();

  async function handle(provider: "google" | "apple") {
    setError(null);
    setLoadingProvider(provider);
    try {
      const idToken = provider === "google"
        ? await signInWithGoogle()
        : await signInWithApple();
      await onToken({ provider, idToken });
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
      <Button
        type="button"
        variant="outline"
        className="w-full gap-2"
        disabled={disabled || isLoading}
        onClick={() => handle("google")}
        aria-label="Continue with Google"
      >
        {loadingProvider === "google"
          ? <Loader2 className="h-4 w-4 animate-spin" />
          : (
            <svg role="img" aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor">
              <path d="M12.48 10.92v3.28h7.84c-.24 1.84-.853 3.187-1.787 4.133-1.147 1.147-2.933 2.4-6.053 2.4-4.827 0-8.6-3.893-8.6-8.72s3.773-8.72 8.6-8.72c2.6 0 4.507 1.027 5.907 2.347l2.307-2.307C18.747 1.44 16.133 0 12.48 0 5.867 0 .307 5.387.307 12s5.56 12 12.173 12c3.573 0 6.267-1.173 8.373-3.36 2.16-2.16 2.84-5.213 2.84-7.667 0-.76-.053-1.467-.173-2.053H12.48z"/>
            </svg>
          )
        }
        Continue with Google
      </Button>

      {/* Apple */}
      <Button
        type="button"
        variant="outline"
        className="w-full gap-2"
        disabled={disabled || isLoading}
        onClick={() => handle("apple")}
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

      {error && (
        <p className="text-xs text-destructive text-center" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
