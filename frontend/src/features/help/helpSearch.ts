import type { HelpArticle, FaqItem, HelpSearchResult } from "./help.types";

export function searchHelp(
  query: string,
  articles: HelpArticle[],
  faqs: FaqItem[],
): HelpSearchResult[] {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return [];

  const results: HelpSearchResult[] = [];

  for (const a of articles) {
    const title = a.title.toLowerCase();
    if (title.includes(q)) {
      results.push({ type: "article", id: a.id, score: 100, matchedOn: "title" });
      continue;
    }
    if (a.keywords.some((k) => k.toLowerCase().includes(q))) {
      results.push({ type: "article", id: a.id, score: 60, matchedOn: "keyword" });
      continue;
    }
    const body = [a.summary, ...a.steps].join(" ").toLowerCase();
    if (body.includes(q)) {
      results.push({ type: "article", id: a.id, score: 30, matchedOn: "body" });
    }
  }

  for (const f of faqs) {
    if (f.question.toLowerCase().includes(q)) {
      results.push({ type: "faq", id: f.id, score: 90, matchedOn: "question" });
    } else if (f.answer.toLowerCase().includes(q)) {
      results.push({ type: "faq", id: f.id, score: 25, matchedOn: "body" });
    }
  }

  return results.sort((a, b) => b.score - a.score);
}
