/**
 * Returns a function that opens the Apple Sign In popup
 * and resolves with the Apple ID token string.
 * Rejects if the SDK is not loaded or the user cancels.
 *
 * No npm packages — relies on window.AppleID injected by the Apple CDN script.
 * Apple Sign In requires HTTPS even in development (use a proxy or ngrok).
 */
export function useAppleSignIn(): () => Promise<string> {
  return () =>
    new Promise<string>((resolve, reject) => {
      if (!window.AppleID?.auth) {
        reject(new Error("Apple Sign-In SDK not loaded."));
        return;
      }

      window.AppleID.auth.init({
        clientId:    import.meta.env.VITE_APPLE_CLIENT_ID as string,
        scope:       "name email",
        redirectURI: window.location.origin,
        usePopup:    true,
      });

      window.AppleID.auth
        .signIn()
        .then((response) => resolve(response.authorization.id_token))
        .catch(() => reject(new Error("Apple sign-in was cancelled or failed.")));
    });
}
