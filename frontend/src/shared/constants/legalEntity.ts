// Single source of truth for the platform's own legal-entity disclosure.
// Read by SiteFooter, the Terms/Privacy page templates, and any future
// invoice/receipt template — never re-type the NIPT or entity name elsewhere.
//
// NOTE: frontend/index.html carries a literal copy of SITE_TAGLINE in its
// static <title>/<meta> tags because a static HTML file cannot import a TS
// constant without a Vite templating plugin this repo does not have. If the two
// ever diverge, THIS FILE wins — update index.html to match, not the reverse.
export const LEGAL_ENTITY_NAME = "Pena e Artë";
export const LEGAL_ENTITY_NIPT = "M12219042B";
export const LEGAL_ENTITY_ADDRESS = ""; // PLACEHOLDER — pending founder input, wire only, do not guess
export const SITE_TAGLINE = "TattooOS — booking & studio management for tattoo shops";
