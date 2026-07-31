import { Link } from "react-router-dom";
import { LEGAL_ENTITY_NAME, LEGAL_ENTITY_NIPT } from "@/shared/constants/legalEntity";

// Site-wide legal footer. Distinct from AuthShellFooter.tsx (which is a generic
// auth-card wrapper — "Already have an account? Sign in"). This one carries the
// platform's trader-identification disclosure (legal entity + NIPT) and links to
// the four policy pages, and is rendered on every public route.
//
// Brand ("TattooOS") stays in the UI; the operating legal entity is disclosed
// below it — the standard header-brand / footer-entity split.

const POLICY_LINKS: ReadonlyArray<{ to: string; label: string }> = [
  { to: "/privacy", label: "Privacy Policy" },
  { to: "/terms", label: "Terms of Service" },
  { to: "/refund-policy", label: "Refund Policy" },
  { to: "/contact", label: "Contact" },
];

export function SiteFooter() {
  const currentYear: number = new Date().getFullYear();

  return (
    <footer
      className="border-t border-border/40 bg-background/95 px-4 py-6 text-center text-xs text-muted-foreground"
      aria-label="Site footer"
    >
      <nav
        aria-label="Legal and policy links"
        className="mb-3 flex flex-wrap items-center justify-center gap-x-4 gap-y-1.5"
      >
        {POLICY_LINKS.map((link) => (
          <Link
            key={link.to}
            to={link.to}
            className="underline-offset-2 transition-colors hover:text-foreground hover:underline"
          >
            {link.label}
          </Link>
        ))}
      </nav>
      <p className="leading-relaxed">
        © {currentYear} TattooOS
        <span aria-hidden="true" className="mx-2 select-none text-border">
          ·
        </span>
        TattooOS is operated by {LEGAL_ENTITY_NAME}, NIPT {LEGAL_ENTITY_NIPT}
      </p>
    </footer>
  );
}
