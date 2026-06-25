import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import type { ReactNode } from "react";
import React from "react";

import authReducer from "@/features/auth/authSlice";
import { hasPermission, usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";

// ── Pure function tests ───────────────────────────────────────────────────────

describe("hasPermission (pure)", () => {
  it("issuer passes all role checks", () => {
    expect(hasPermission(Role.Issuer, Role.Client)).toBe(true);
    expect(hasPermission(Role.Issuer, Role.Artist)).toBe(true);
    expect(hasPermission(Role.Issuer, Role.Owner)).toBe(true);
    expect(hasPermission(Role.Issuer, Role.Issuer)).toBe(true);
  });

  it("owner passes owner, artist, client checks", () => {
    expect(hasPermission(Role.Owner, Role.Client)).toBe(true);
    expect(hasPermission(Role.Owner, Role.Artist)).toBe(true);
    expect(hasPermission(Role.Owner, Role.Owner)).toBe(true);
  });

  it("owner fails issuer check", () => {
    expect(hasPermission(Role.Owner, Role.Issuer)).toBe(false);
  });

  it("artist passes artist, client checks", () => {
    expect(hasPermission(Role.Artist, Role.Client)).toBe(true);
    expect(hasPermission(Role.Artist, Role.Artist)).toBe(true);
  });

  it("artist fails owner check", () => {
    expect(hasPermission(Role.Artist, Role.Owner)).toBe(false);
  });

  it("client passes client check only", () => {
    expect(hasPermission(Role.Client, Role.Client)).toBe(true);
    expect(hasPermission(Role.Client, Role.Artist)).toBe(false);
  });

  it("null role fails all checks", () => {
    expect(hasPermission(null, Role.Client)).toBe(false);
    expect(hasPermission(null, Role.Issuer)).toBe(false);
  });
});

// ── Hook tests ────────────────────────────────────────────────────────────────

function makeStore(role: string | null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role } as any,
    },
  });
}

function makeWrapper(role: string | null) {
  const store = makeStore(role);
  return function Wrapper({ children }: { children: ReactNode }) {
    return React.createElement(Provider, { store, children });
  };
}

describe("usePermission (hook)", () => {
  it("returns true when authenticated role meets requirement", () => {
    const { result } = renderHook(() => usePermission(Role.Artist), {
      wrapper: makeWrapper(Role.Owner),
    });
    expect(result.current).toBe(true);
  });

  it("returns false when authenticated role is insufficient", () => {
    const { result } = renderHook(() => usePermission(Role.Owner), {
      wrapper: makeWrapper(Role.Client),
    });
    expect(result.current).toBe(false);
  });

  it("returns false when role is null (unauthenticated)", () => {
    const { result } = renderHook(() => usePermission(Role.Client), {
      wrapper: makeWrapper(null),
    });
    expect(result.current).toBe(false);
  });
});
