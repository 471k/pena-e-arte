/**
 * Returns a function that opens the Google One Tap / popup flow
 * and resolves with the Google ID token (credential) string.
 * Rejects if the SDK is not loaded or the user closes the popup.
 *
 * No npm packages — relies on window.google injected by the GSI CDN script.
 */
export function useGoogleSignIn(): () => Promise<string> {
  return () =>
    new Promise<string>((resolve, reject) => {
      if (!window.google?.accounts?.id) {
        reject(new Error("Google Sign-In SDK not loaded."));
        return;
      }

      window.google.accounts.id.initialize({
        client_id:             import.meta.env.VITE_GOOGLE_CLIENT_ID as string,
        callback:              ({ credential }) => resolve(credential),
        auto_select:           false,
        cancel_on_tap_outside: true,
      });

      window.google.accounts.id.prompt((notification) => {
        if (notification.isNotDisplayed() || notification.isSkippedMoment()) {
          reject(new Error("Google sign-in was dismissed or not displayed."));
        }
      });
    });
}
