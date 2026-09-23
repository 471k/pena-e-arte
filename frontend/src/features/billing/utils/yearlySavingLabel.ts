// D7 — the yearly saving is computed from the two real prices, never typed or read from
// Plan.yearlyDiscountPercent (admin-input-only after this). See architecture.md
// Decisions Log — "Four-tier catalogue + yearly on every paid tier".

// Floor, so the label can never overstate the saving.
export function yearlySavingPercentFloor(monthly: number, yearly: number): number | null {
  if (monthly <= 0) return null;
  const saving = monthly * 12 - yearly;
  if (saving <= 0) return null;
  return Math.floor((1 - yearly / (monthly * 12)) * 100);
}

export function yearlySavingLabel(monthly: number, yearly: number): string | null {
  if (monthly <= 0) return null;
  const saving = monthly * 12 - yearly;
  if (saving <= 0) return null;

  const monthsFree = Math.round((saving / monthly) * 100) / 100;
  if (Number.isInteger(monthsFree)) {
    return monthsFree === 1 ? "1 month free" : `${monthsFree} months free`;
  }

  const percent = yearlySavingPercentFloor(monthly, yearly);
  return percent !== null ? `save ${percent}%` : null;
}
