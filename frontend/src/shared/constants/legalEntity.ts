// Single source of truth for the platform's own legal-entity disclosure.
// Read by SiteFooter, the Terms/Privacy page templates, and any future
// invoice/receipt template — never re-type the NIPT or entity name elsewhere.
//
// NOTE: frontend/index.html carries a literal copy of SITE_TAGLINE and
// SITE_META_DESCRIPTION in its static <title>/<meta> tags because a static HTML
// file cannot import a TS constant without a Vite templating plugin this repo
// does not have. If the two ever diverge, THIS FILE wins — update index.html to
// match, not the reverse.
export const LEGAL_ENTITY_NAME = "Pena e Artë";
export const LEGAL_ENTITY_NIPT = "M12219042B";
export const LEGAL_ENTITY_ADDRESS = "Rruga Pirro Goda, Tiranë, Albania";

// Short brand line — used for <title> and og:title.
export const SITE_TAGLINE = "TattooOS — booking & studio management for tattoo shops";

// Longer SEO copy — used for <meta name="description"> and og:description.
// Deliberately distinct from SITE_TAGLINE (search snippets want a benefit-led
// sentence, not the brand line).
export const SITE_META_DESCRIPTION =
  "TattooOS — booking, deposits, consent forms, and client records for tattoo studios. Ditch the DMs and paper forms.";
