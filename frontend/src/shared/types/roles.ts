export const Role = {
  Client: "client",
  Artist: "artist",
  Owner: "owner",
  Issuer: "issuer",
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export interface User {
  id: string;
  email: string;
  name?: string;
}

export interface AuthPayload {
  user:         User;
  token:        string;
  refreshToken?: string | null;
  tenantId:     string | null;
  role:         Role;
}
