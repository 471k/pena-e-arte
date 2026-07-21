import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Provider } from "react-redux";
import { RouterProvider } from "react-router-dom";
import { Toaster } from "./shared/components/ui/sonner";
import { ErrorBoundary } from "./shared/components/ErrorBoundary";
import { CookieConsentBanner } from "./shared/components/CookieConsentBanner";
import { store } from "./app/store";
import { router } from "./app/router";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ErrorBoundary>
      <Provider store={store}>
        <RouterProvider router={router} />
        <Toaster />
        <CookieConsentBanner />
      </Provider>
    </ErrorBoundary>
  </StrictMode>
);
