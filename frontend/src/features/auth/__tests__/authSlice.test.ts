import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";

// ── JWT helper ─────────────────────────────────────────────────────────────────
// Matches the structure that the real backend and fake-jwt.mjs produce.

const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function toBase64Url(s: string) {
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function makeToken(claims: Record<string, unknown>): string {
  const header  = toBase64Url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = toBase64Url(JSON.stringify(claims));
  return `${header}.${payload}.fakesig`;
}

const EXP_FUTURE = Math.floor(Date.now() / 1000) + 3_600;
const EXP_PAST   = Math.floor(Date.now() / 1000) - 3_600;

const VALID_CLAIMS = {
  sub:          "user-0001",
  email:        "owner@test.com",
  tenant_id:    "studio-0001",
  [ROLE_CLAIM]: "owner",
  exp:          EXP_FUTURE,
};

// ── Helper: build a fresh store with a pre-seeded localStorage ─────────────────
// vi.resetModules() + dynamic import forces loadInitialState to re-run with the
// current localStorage contents — the only reliable way to test boot-time logic.

async function makeStore(token: string | null) {
  vi.resetModules();
  localStorage.clear();
  if (token !== null) localStorage.setItem("auth_token", token);

  const [{ default: authReducer }, { configureStore }] = await Promise.all([
    import("@/features/auth/authSlice"),
    import("@reduxjs/toolkit"),
  ]);
  return configureStore({ reducer: { auth: authReducer } });
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("authSlice — loadInitialState (token bootstrap)", () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  // ── No token ─────────────────────────────────────────────────────────────────

  it("no token in localStorage → EMPTY state", async () => {
    const store = await makeStore(null);
    const s = store.getState().auth;
    expect(s.user).toBeNull();
    expect(s.token).toBeNull();
    expect(s.role).toBeNull();
    expect(s.tenantId).toBeNull();
  });

  // ── Happy path ────────────────────────────────────────────────────────────────

  it("valid non-expired token → hydrates user, role, tenantId", async () => {
    const token = makeToken(VALID_CLAIMS);
    const store = await makeStore(token);
    const s = store.getState().auth;
    expect(s.user?.email).toBe("owner@test.com");
    expect(s.role).toBe("owner");
    expect(s.tenantId).toBe("studio-0001");
    expect(s.token).toBe(token);
  });

  // ── Expired token ─────────────────────────────────────────────────────────────

  it("expired token → EMPTY state", async () => {
    const store = await makeStore(makeToken({ ...VALID_CLAIMS, exp: EXP_PAST }));
    const s = store.getState().auth;
    expect(s.user).toBeNull();
    expect(s.token).toBeNull();
  });

  it("expired token → removed from localStorage", async () => {
    await makeStore(makeToken({ ...VALID_CLAIMS, exp: EXP_PAST }));
    expect(localStorage.getItem("auth_token")).toBeNull();
  });

  // ── No-exp token ──────────────────────────────────────────────────────────────

  it("token without exp claim → treated as non-expiring, state hydrated", async () => {
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { exp, ...withoutExp } = VALID_CLAIMS;
    const store = await makeStore(makeToken(withoutExp));
    expect(store.getState().auth.role).toBe("owner");
  });

  // ── Malformed / corrupted token ────────────────────────────────────────────────

  it("malformed token string → EMPTY state (parse error caught)", async () => {
    const store = await makeStore("not.a.jwt");
    expect(store.getState().auth.user).toBeNull();
  });

  it("token with unparseable payload → EMPTY state", async () => {
    const store = await makeStore("header.!!!.sig");
    expect(store.getState().auth.user).toBeNull();
  });

  // ── Role normalisation (exercised via loadInitialState) ────────────────────────

  it("unknown role in token → falls back to 'client'", async () => {
    const store = await makeStore(
      makeToken({ ...VALID_CLAIMS, [ROLE_CLAIM]: "superadmin" })
    );
    expect(store.getState().auth.role).toBe("client");
  });

  it("missing role claim → falls back to 'client'", async () => {
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { [ROLE_CLAIM]: _, ...withoutRole } = VALID_CLAIMS;
    const store = await makeStore(makeToken(withoutRole));
    expect(store.getState().auth.role).toBe("client");
  });
});

// ── Reducer tests ──────────────────────────────────────────────────────────────

describe("authSlice — reducers", () => {
  beforeEach(() => {
    vi.resetModules();
    localStorage.clear();
  });
  afterEach(() => localStorage.clear());

  it("setCredentials stores token in localStorage and updates state", async () => {
    const { default: authReducer, setCredentials } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    const token = makeToken(VALID_CLAIMS);
    store.dispatch(setCredentials({
      user:     { id: "user-0001", email: "owner@test.com" },
      token,
      tenantId: "studio-0001",
      role:     "owner",
    }));

    const s = store.getState().auth;
    expect(s.token).toBe(token);
    expect(s.role).toBe("owner");
    expect(localStorage.getItem("auth_token")).toBe(token);
  });

  it("logout clears state and removes token from localStorage", async () => {
    const token = makeToken(VALID_CLAIMS);
    localStorage.setItem("auth_token", token);
    const { default: authReducer, logout } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    store.dispatch(logout());

    expect(store.getState().auth.user).toBeNull();
    expect(store.getState().auth.token).toBeNull();
    expect(localStorage.getItem("auth_token")).toBeNull();
  });

  it("setPendingReferralCode stores and clears the code", async () => {
    const { default: authReducer, setPendingReferralCode } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    store.dispatch(setPendingReferralCode("REF-123"));
    expect(store.getState().auth.pendingReferralCode).toBe("REF-123");

    store.dispatch(setPendingReferralCode(null));
    expect(store.getState().auth.pendingReferralCode).toBeNull();
  });
});

// ── Support Impersonation ───────────────────────────────────────────────────────

describe("authSlice — startImpersonation / endImpersonation", () => {
  const ADMIN_CLAIMS = {
    sub:          "admin-0001",
    email:        "admin@test.com",
    [ROLE_CLAIM]: "admin",
    exp:          EXP_FUTURE,
  };

  beforeEach(() => {
    vi.resetModules();
    localStorage.clear();
    sessionStorage.clear();
  });
  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it("startImpersonation swaps the active token and sets impersonation meta", async () => {
    const adminToken = makeToken(ADMIN_CLAIMS);
    const { default: authReducer, setCredentials, startImpersonation } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });
    store.dispatch(setCredentials({
      user: { id: "admin-0001", email: "admin@test.com" },
      token: adminToken, tenantId: null, role: "admin" as never,
    }));

    const impToken = makeToken({
      sub: "admin-0001", email: "admin@test.com", [ROLE_CLAIM]: "admin",
      tenant_id: "studio-target", imp: "session-123", exp: EXP_FUTURE,
    });

    store.dispatch(startImpersonation({
      accessToken: impToken,
      sessionId: "session-123",
      studioId: "studio-target",
      studioName: "Ink & Iron",
      expiresAt: new Date(EXP_FUTURE * 1000).toISOString(),
    }));

    const s = store.getState().auth;
    expect(s.token).toBe(impToken);
    expect(s.tenantId).toBe("studio-target");
    expect(s.role).toBe("admin");
    expect(s.impersonation).toEqual({
      sessionId: "session-123", studioId: "studio-target",
      studioName: "Ink & Iron", expiresAt: new Date(EXP_FUTURE * 1000).toISOString(),
    });
    expect(sessionStorage.getItem("auth_token")).toBe(impToken);
  });

  it("startImpersonation stashes the real token so it can be restored later", async () => {
    const adminToken = makeToken(ADMIN_CLAIMS);
    const { default: authReducer, setCredentials, startImpersonation } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });
    store.dispatch(setCredentials({
      user: { id: "admin-0001", email: "admin@test.com" },
      token: adminToken, tenantId: null, role: "admin" as never,
    }));

    const impToken = makeToken({
      sub: "admin-0001", email: "admin@test.com", [ROLE_CLAIM]: "admin",
      tenant_id: "studio-target", imp: "session-123", exp: EXP_FUTURE,
    });
    store.dispatch(startImpersonation({
      accessToken: impToken, sessionId: "session-123", studioId: "studio-target",
      studioName: "Ink & Iron", expiresAt: new Date(EXP_FUTURE * 1000).toISOString(),
    }));

    const stash = JSON.parse(sessionStorage.getItem("auth_impersonation_stash")!);
    expect(stash.token).toBe(adminToken);
  });

  it("endImpersonation restores the stashed admin token and clears impersonation state", async () => {
    const adminToken = makeToken(ADMIN_CLAIMS);
    const { default: authReducer, setCredentials, startImpersonation, endImpersonation } =
      await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });
    store.dispatch(setCredentials({
      user: { id: "admin-0001", email: "admin@test.com" },
      token: adminToken, tenantId: null, role: "admin" as never,
    }));
    const impToken = makeToken({
      sub: "admin-0001", email: "admin@test.com", [ROLE_CLAIM]: "admin",
      tenant_id: "studio-target", imp: "session-123", exp: EXP_FUTURE,
    });
    store.dispatch(startImpersonation({
      accessToken: impToken, sessionId: "session-123", studioId: "studio-target",
      studioName: "Ink & Iron", expiresAt: new Date(EXP_FUTURE * 1000).toISOString(),
    }));

    store.dispatch(endImpersonation());

    const s = store.getState().auth;
    expect(s.token).toBe(adminToken);
    expect(s.impersonation).toBeNull();
    expect(sessionStorage.getItem("auth_impersonation_stash")).toBeNull();
    expect(sessionStorage.getItem("auth_impersonation_meta")).toBeNull();
  });

  it("endImpersonation with no stash present falls back to a full logout", async () => {
    const { default: authReducer, endImpersonation } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    store.dispatch(endImpersonation());

    const s = store.getState().auth;
    expect(s.token).toBeNull();
    expect(s.impersonation).toBeNull();
  });

  it("loadInitialState restores impersonation meta from sessionStorage on reload", async () => {
    const meta = {
      sessionId: "session-123", studioId: "studio-target",
      studioName: "Ink & Iron", expiresAt: new Date(EXP_FUTURE * 1000).toISOString(),
    };
    sessionStorage.setItem("auth_impersonation_meta", JSON.stringify(meta));
    const impToken = makeToken({
      sub: "admin-0001", email: "admin@test.com", [ROLE_CLAIM]: "admin",
      tenant_id: "studio-target", imp: "session-123", exp: EXP_FUTURE,
    });
    sessionStorage.setItem("auth_token", impToken);

    const { default: authReducer } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    expect(store.getState().auth.impersonation).toEqual(meta);
  });

  it("loadInitialState without impersonation meta still shows the banner defensively", async () => {
    // Meta missing (e.g. a different tab) — the token still carries "imp", so the app
    // must never silently render a normal admin view while holding this token.
    const impToken = makeToken({
      sub: "admin-0001", email: "admin@test.com", [ROLE_CLAIM]: "admin",
      tenant_id: "studio-target", imp: "session-123", exp: EXP_FUTURE,
    });
    sessionStorage.setItem("auth_token", impToken);

    const { default: authReducer } = await import("@/features/auth/authSlice");
    const { configureStore } = await import("@reduxjs/toolkit");
    const store = configureStore({ reducer: { auth: authReducer } });

    expect(store.getState().auth.impersonation).not.toBeNull();
    expect(store.getState().auth.impersonation?.sessionId).toBe("session-123");
  });
});
