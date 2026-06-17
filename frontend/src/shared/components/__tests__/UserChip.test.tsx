import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import { UserChip } from "@/shared/components/UserChip";
import type { User } from "@/shared/types/roles";

function makeStore(user: User | null = null, role: string | null = null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user, token: null, tenantId: null, role } as any,
    },
  });
}

function renderChip(user: User | null = null, role: string | null = null) {
  render(
    <Provider store={makeStore(user, role)}>
      <UserChip />
    </Provider>,
  );
}

describe("UserChip", () => {
  it("renders the user's display name", () => {
    renderChip({ id: "u1", email: "test@example.com", name: "Alice Smith" }, "client");
    expect(screen.getByText("Alice Smith")).toBeInTheDocument();
  });

  it("renders initials when no avatar image is provided", () => {
    renderChip({ id: "u1", email: "bob@example.com", name: "Bob Jones" }, "artist");
    expect(screen.getByText("B")).toBeInTheDocument();
  });

  it("falls back to email prefix when name is absent", () => {
    renderChip({ id: "u1", email: "carol@example.com" }, "owner");
    expect(screen.getByText("carol")).toBeInTheDocument();
  });

  it("renders nothing when user is null", () => {
    const { container } = render(
      <Provider store={makeStore(null)}>
        <UserChip />
      </Provider>,
    );
    expect(container.firstChild).toBeNull();
  });

  it("shows role label", () => {
    renderChip({ id: "u1", email: "owner@example.com", name: "Dana" }, "owner");
    expect(screen.getByText("Owner")).toBeInTheDocument();
  });
});
