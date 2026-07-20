import { describe, it, expect } from "vitest";
import { searchHelp } from "../helpSearch";
import type { HelpArticle, FaqItem } from "../help.types";

const articles: HelpArticle[] = [
  {
    id: "a1",
    roles: ["client"],
    title: "Book an appointment",
    keywords: ["booking", "schedule"],
    summary: "Request a tattoo appointment.",
    steps: ["Pick an artist", "Pick a date"],
  },
  {
    id: "a2",
    roles: ["client"],
    title: "View your designs",
    keywords: ["artwork"],
    summary: "See design drafts from your artist here in the review area.",
    steps: ["Open Designs"],
  },
];

const faqs: FaqItem[] = [
  {
    id: "f1",
    roles: ["client"],
    question: "How long is the free trial?",
    answer: "14 days.",
  },
];

describe("searchHelp", () => {
  it("returns empty array for queries under 2 characters", () => {
    expect(searchHelp("b", articles, faqs)).toEqual([]);
    expect(searchHelp("", articles, faqs)).toEqual([]);
  });

  it("ranks title matches above keyword matches", () => {
    const results = searchHelp("book", articles, faqs);
    expect(results[0]).toMatchObject({ id: "a1", matchedOn: "title" });
  });

  it("ranks keyword matches above body matches", () => {
    const results = searchHelp("artwork", articles, faqs);
    expect(results[0]).toMatchObject({ id: "a2", matchedOn: "keyword" });
  });

  it("matches are case-insensitive", () => {
    const results = searchHelp("BOOK", articles, faqs);
    expect(results[0]).toMatchObject({ id: "a1" });
  });

  it("returns faq question matches", () => {
    const results = searchHelp("free trial", articles, faqs);
    expect(results.some((r) => r.type === "faq" && r.id === "f1")).toBe(true);
  });

  it("matches body text when no title or keyword matches", () => {
    const results = searchHelp("review area", articles, faqs);
    expect(results[0]).toMatchObject({ id: "a2", matchedOn: "body" });
  });
});
