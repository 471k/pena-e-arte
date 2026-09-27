import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { routes } from "@/app/router";

// Guard: every route the app can serve must be either explicitly public-and-indexable or covered by
// the nginx `noindex` map (frontend/nginx.conf.template). A new route that is in neither fails here,
// naming the path, so a private page can never silently become indexable.

/** First path segments that are meant to be indexed. "" is the home page. */
const PUBLIC_INDEXABLE: ReadonlySet<string> = new Set([
  "",
  "features",
  "pricing",
  "use",
  "faq",
  "contact",
  "privacy",
  "terms",
  "refund-policy",
  "discover",
  "map",
  "s",
  "artist",
]);

interface RouteNode {
  path?: string;
  children?: RouteNode[];
}

function joinPaths(prefix: string, path: string): string {
  if (path.startsWith("/")) return path;
  return `${prefix.replace(/\/$/, "")}/${path}`;
}

function collectPaths(nodes: ReadonlyArray<RouteNode>, prefix: string, out: Set<string>): void {
  for (const node of nodes) {
    const full = node.path ? joinPaths(prefix, node.path) : prefix;
    if (node.path) out.add(full);
    if (node.children) collectPaths(node.children, full, out);
  }
}

function firstSegments(): string[] {
  const all = new Set<string>();
  collectPaths(routes as unknown as RouteNode[], "", all);
  const segments = new Set<string>();
  for (const path of all) {
    if (path === "*" || path.includes("*")) continue;
    const first = path.split("/").filter(Boolean)[0] ?? "";
    if (first.startsWith(":")) continue; // a bare param route has no fixed prefix to classify
    segments.add(first);
  }
  return [...segments].sort();
}

/** The alternations of the case-insensitive private-route regexes in nginx's `map`. */
function nginxPrivateMatchers(): RegExp[] {
  const template = readFileSync(resolve(import.meta.dirname, "../../nginx.conf.template"), "utf8");
  const mapBlock = template.slice(template.indexOf("map $request_uri $robots_tag"));
  const mapEnd = mapBlock.indexOf("\n}");
  const body = mapBlock.slice(0, mapEnd);
  return [...body.matchAll(/~\*\^\/\(([^)]+)\)/g)].map((m) => new RegExp(`^/(${m[1]})(/|\\?|$)`, "i"));
}

describe("SEO route coverage", () => {
  const matchers = nginxPrivateMatchers();
  const isPrivate = (path: string): boolean => matchers.some((re) => re.test(path));

  it("finds the nginx noindex regexes and the app's routes", () => {
    expect(matchers.length).toBe(2);
    expect(firstSegments().length).toBeGreaterThan(20);
  });

  it("classifies every route as public-indexable or noindex in nginx", () => {
    const unclassified = firstSegments().filter((segment) => !PUBLIC_INDEXABLE.has(segment) && !isPrivate(`/${segment}`));

    expect(
      unclassified,
      `These first path segments are neither in PUBLIC_INDEXABLE (this test) nor matched by the noindex map ` +
        `in frontend/nginx.conf.template: ${unclassified.join(", ")}. Decide: private => add to the map; ` +
        `public => add to PUBLIC_INDEXABLE and to the sitemap/manifest if it should be listed.`,
    ).toEqual([]);
  });

  it("never marks a public-indexable route noindex", () => {
    for (const segment of PUBLIC_INDEXABLE) {
      if (segment === "") continue;
      expect(isPrivate(`/${segment}`), `/${segment} is public but matches the noindex map`).toBe(false);
    }
  });

  it("does not let a public route's nested path be caught by a private prefix", () => {
    expect(isPrivate("/s/some-studio/gift-cards/buy")).toBe(false);
    expect(isPrivate("/artist/some-artist")).toBe(false);
  });

  it("matches private routes case-insensitively and with a query string", () => {
    expect(isPrivate("/Dashboard")).toBe(true);
    expect(isPrivate("/dashboard?tab=1")).toBe(true);
    expect(isPrivate("/dashboard/settings")).toBe(true);
  });

  it("does not match a private word that is only a prefix of a longer segment", () => {
    expect(isPrivate("/dashboards-are-fun")).toBe(false);
  });
});
