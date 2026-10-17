import { useCallback } from "react";
import { useLocation, useNavigate } from "react-router-dom";

/**
 * "Back" that never leaves TattooOS. History-back is right when the user came from another page
 * in the app; when the page was opened directly (a link in an email or notification, a new tab),
 * there is no in-app page behind it and navigate(-1) would leave the site — so go to `fallback`.
 * React Router marks the first location of a session with key "default".
 */
export function useGoBack(fallback: string): () => void {
  const navigate = useNavigate();
  const location = useLocation();
  return useCallback(() => {
    if (location.key !== "default") navigate(-1);
    else navigate(fallback, { replace: true });
  }, [fallback, location.key, navigate]);
}
