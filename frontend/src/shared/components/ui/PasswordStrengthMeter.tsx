interface Props {
  password: string;
}

type Strength = "weak" | "fair" | "good" | "strong";

function getStrength(pw: string): Strength | null {
  if (!pw) return null;
  const len        = pw.length;
  const hasUpper   = /[A-Z]/.test(pw);
  const hasLower   = /[a-z]/.test(pw);
  const hasDigit   = /\d/.test(pw);
  const hasSpecial = /[^A-Za-z0-9]/.test(pw);

  if (len < 8)                                         return "weak";
  if (len < 10 || !(hasUpper && hasLower && hasDigit))  return "fair";
  if (len < 12 || !hasSpecial)                          return "good";
  return "strong";
}

const LABELS: Record<Strength, string> = {
  weak:   "Weak",
  fair:   "Fair",
  good:   "Good",
  strong: "Strong",
};

const COLORS: Record<Strength, string> = {
  weak:   "bg-destructive",
  fair:   "bg-amber-500",
  good:   "bg-emerald-400",
  strong: "bg-emerald-500",
};

const WIDTHS: Record<Strength, string> = {
  weak:   "w-1/4",
  fair:   "w-2/4",
  good:   "w-3/4",
  strong: "w-full",
};

export function PasswordStrengthMeter({ password }: Props) {
  const strength = getStrength(password);

  if (!strength) return null;

  return (
    <div className="space-y-1" aria-live="polite" aria-label={`Password strength: ${LABELS[strength]}`}>
      <div className="h-1 rounded-full bg-border overflow-hidden">
        <div
          className={`h-full rounded-full transition-all duration-300 ${COLORS[strength]} ${WIDTHS[strength]}`}
        />
      </div>
      <p className="text-[11px] text-muted-foreground">
        Strength:{" "}
        <span className={`font-medium ${strength === "weak" ? "text-destructive" : ""}`}>
          {LABELS[strength]}
        </span>
        {strength === "weak" && " — use at least 8 characters"}
        {strength === "fair" && " — add uppercase, lowercase, and a number"}
        {strength === "good" && " — add a symbol (!@#…) to make it strong"}
      </p>
    </div>
  );
}
