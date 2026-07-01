import { describe, it, expect } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import type { ReactNode } from "react";
import React from "react";

import authReducer, { setCredentials, logout } from "@/features/auth/authSlice";
import { useCurrentUser } from "@/shared/hooks/useCurrentUser";
import type { User, AuthPayload } from "@/shared/types/roles";

const USER: User = { id: "u1", email: "alice@ink.test", name: "Alice" };

const PAYLOAD: AuthPayload = {
  user:         USER,
  token:        "fake-token",
  tenantId:     "t1",
  role:         "client",
  refreshToken: null,
};

function makeStore(user: User | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user, token: null, tenantId: null, role: null } as any,
    },
  });
}

function makeWrapper(store: ReturnType<typeof makeStore>) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return React.createElement(Provider, { store, children });
  };
}

describe("useCurrentUser", () => {
  it("returns null when not authenticated", () => {
    const store = makeStore(null);
    const { result } = renderHook(() => useCurrentUser(), { wrapper: makeWrapper(store) });
    expect(result.current).toBeNull();
  });

  it("returns the user object when authenticated", () => {
    const store = makeStore(USER);
    const { result } = renderHook(() => useCurrentUser(), { wrapper: makeWrapper(store) });
    expect(result.current).toEqual(USER);
  });

  it("returns updated user after state changes", () => {
    const store = makeStore(null);
    const { result } = renderHook(() => useCurrentUser(), { wrapper: makeWrapper(store) });
    expect(result.current).toBeNull();

    act(() => { store.dispatch(setCredentials(PAYLOAD)); });
    expect(result.current).toEqual(USER);

    act(() => { store.dispatch(logout()); });
    expect(result.current).toBeNull();
  });
});
