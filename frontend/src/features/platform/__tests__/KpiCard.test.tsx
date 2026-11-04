import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { KpiCard, type KpiAccent } from "../components/KpiCard";

// Accessibility cleanup (2026-11-04): the accent icon colours moved from hard-coded
// text-blue-500 / -amber-500 / -emerald-500 / -red-500 (2.19–3.38:1 on white, under even the
// 3:1 icon bar) to the semantic tokens. The label and value still render as text beside it.
describe("KpiCard accent colours", () => {
  it.each<[KpiAccent, string]>([
    ["default", "text-muted-foreground"],
    ["info", "text-info-text"],
    ["warning", "text-warning-text"],
    ["success", "text-success-text"],
    ["danger", "text-destructive-text"],
  ])("%s accent uses %s and keeps its label and value", (accent, cls) => {
    render(
      <MemoryRouter>
        <KpiCard label="Past due" value={3} accent={accent} icon={<svg data-testid="icon" aria-hidden="true" />} />
      </MemoryRouter>,
    );
    expect(screen.getByText("Past due")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByTestId("icon").parentElement).toHaveClass(cls);
  });
});
