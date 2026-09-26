// Pure HTML renderer for the per-route <head> metadata. Used by scripts/prerender-meta.ts at build
// time and unit-tested directly. Dependency-free apart from a type import (erased) and a relative
// value import with the .ts extension, so Node 24 can run it via type stripping.

import { DEFAULT_OG_IMAGE, SITE_URL } from "./siteRoutes.ts";
import type { RouteMeta } from "./siteRoutes.ts";

export const HEAD_START_MARKER = "<!-- seo:head:start -->";
export const HEAD_END_MARKER = "<!-- seo:head:end -->";

// Tags carry data-doc-meta so useDocumentMeta's cleanup replaces them instead of duplicating them.
const TAG = 'data-doc-meta="1"';

export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

export function renderHead(route: RouteMeta): string {
  const url = `${SITE_URL}${route.path === "/" ? "/" : route.path}`;
  const title = escapeHtml(route.title);
  const description = escapeHtml(route.description);
  const image = escapeHtml(DEFAULT_OG_IMAGE);
  const canonical = escapeHtml(url);

  return [
    `<title>${title}</title>`,
    `<meta ${TAG} name="description" content="${description}" />`,
    `<link ${TAG} rel="canonical" href="${canonical}" />`,
    `<meta ${TAG} property="og:type" content="website" />`,
    `<meta ${TAG} property="og:url" content="${canonical}" />`,
    `<meta ${TAG} property="og:title" content="${title}" />`,
    `<meta ${TAG} property="og:description" content="${description}" />`,
    `<meta ${TAG} property="og:image" content="${image}" />`,
    `<meta ${TAG} name="twitter:card" content="summary_large_image" />`,
    `<meta ${TAG} name="twitter:title" content="${title}" />`,
    `<meta ${TAG} name="twitter:description" content="${description}" />`,
    `<meta ${TAG} name="twitter:image" content="${image}" />`,
  ].join("\n    ");
}

/**
 * Replaces the marked head region of index.html with the route's own metadata. Throws when either
 * marker is missing, so a template edit that drops them fails the build instead of silently
 * shipping the default metadata for every route.
 */
export function renderRouteHtml(template: string, route: RouteMeta): string {
  const start = template.indexOf(HEAD_START_MARKER);
  const end = template.indexOf(HEAD_END_MARKER);
  if (start === -1 || end === -1 || end < start) {
    throw new Error(
      `index.html is missing the "${HEAD_START_MARKER}" / "${HEAD_END_MARKER}" markers`,
    );
  }
  const before = template.slice(0, start + HEAD_START_MARKER.length);
  const after = template.slice(end);
  return `${before}\n    ${renderHead(route)}\n    ${after}`;
}
