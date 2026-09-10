import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Provider } from "react-redux";
import { RouterProvider } from "react-router-dom";
import { Toaster } from "./shared/components/ui/sonner";
import { ErrorBoundary } from "./shared/components/ErrorBoundary";
import { CookieConsentBanner } from "./shared/components/CookieConsentBanner";
import { StagingBanner } from "./shared/components/StagingBanner";
import { TrafficBeacon } from "./shared/hooks/useTrafficBeacon";
import { store } from "./app/store";
import { router } from "./app/router";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ErrorBoundary>
      <Provider store={store}>
        <StagingBanner />
        <RouterProvider router={router} />
        <Toaster />
        <CookieConsentBanner />
        <TrafficBeacon />
      </Provider>
    </ErrorBoundary>
  </StrictMode>
);

// Installable PWA (backlog item 7) — registered after the initial render so it never blocks
// first paint. Caches only the static app shell (see sw.js); never intercepts /api/ or /hubs/.
if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch((err) => {
      console.error("Service worker registration failed:", err);
    });
  });
}
