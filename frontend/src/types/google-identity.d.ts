// Minimal type declarations for Google Identity Services (accounts.google.com/gsi/client).
// Only the surface we actually call is typed here.

interface CredentialResponse {
  credential: string;
  select_by:  string;
  client_id:  string;
}

interface PromptMomentNotification {
  isNotDisplayed():        boolean;
  isSkippedMoment():       boolean;
  isDismissedMoment():     boolean;
  getNotDisplayedReason(): string;
  getSkippedReason():      string;
  getDismissedReason():    string;
}

interface IdConfiguration {
  client_id:             string;
  callback:               (response: CredentialResponse) => void;
  auto_select?:           boolean;
  cancel_on_tap_outside?: boolean;
}

interface GsiButtonConfiguration {
  type:   "standard" | "icon";
  theme?: "outline" | "filled_blue" | "filled_black";
  size?:  "large" | "medium" | "small";
  text?:  "signin_with" | "signup_with" | "continue_with" | "signin";
  shape?: "rectangular" | "pill" | "circle" | "square";
  width?: number;
}

interface Google {
  accounts: {
    id: {
      initialize(config: IdConfiguration): void;
      prompt(callback?: (notification: PromptMomentNotification) => void): void;
      renderButton(element: HTMLElement, config: GsiButtonConfiguration): void;
      disableAutoSelect(): void;
      cancel(): void;
    };
  };
}

declare global {
  interface Window {
    google?: Google;
  }
}

export {};
