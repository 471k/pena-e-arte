import { describe, it, expect } from "vitest";
import { HELP_ARTICLES, FAQ_ITEMS } from "../helpContent";

describe("helpContent", () => {
  it("has no duplicate article ids", () => {
    const ids = HELP_ARTICLES.map((a) => a.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("has no duplicate faq ids", () => {
    const ids = FAQ_ITEMS.map((f) => f.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("every relatedArticleId references a real article", () => {
    const articleIds = new Set(HELP_ARTICLES.map((a) => a.id));
    for (const article of HELP_ARTICLES) {
      for (const relatedId of article.relatedArticleIds ?? []) {
        expect(articleIds.has(relatedId)).toBe(true);
      }
    }
    for (const faq of FAQ_ITEMS) {
      for (const relatedId of faq.relatedArticleIds ?? []) {
        expect(articleIds.has(relatedId)).toBe(true);
      }
    }
  });

  it("every article has at least one role", () => {
    for (const article of HELP_ARTICLES) {
      expect(article.roles.length).toBeGreaterThan(0);
    }
  });

  it("every faq has at least one role", () => {
    for (const faq of FAQ_ITEMS) {
      expect(faq.roles.length).toBeGreaterThan(0);
    }
  });

  it("every article with a route starts with '/'", () => {
    for (const article of HELP_ARTICLES) {
      if (article.route) {
        expect(article.route.startsWith("/")).toBe(true);
      }
    }
  });

  it("has at least 18 FAQ items", () => {
    expect(FAQ_ITEMS.length).toBeGreaterThanOrEqual(18);
  });
});
