// Minimal type declarations for Apple Sign In JS SDK.

interface AppleSignInAuthorization {
  code:     string;
  id_token: string;
  state:    string;
}

interface AppleSignInUser {
  email?: string;
  name?: {
    firstName?: string;
    lastName?:  string;
  };
}

interface AppleSignInResponse {
  authorization: AppleSignInAuthorization;
  user?:         AppleSignInUser;
}

interface AppleIDAuthConfig {
  clientId:    string;
  scope:       string;
  redirectURI: string;
  state?:      string;
  usePopup?:   boolean;
}

interface AppleIDAuth {
  init(config: AppleIDAuthConfig): void;
  signIn(): Promise<AppleSignInResponse>;
}

declare global {
  interface Window {
    AppleID?: {
      auth: AppleIDAuth;
    };
  }
}

export {};
