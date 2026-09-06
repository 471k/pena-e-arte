export const HelpRole = {
  Client: "client",
  Artist: "artist",
  Owner:  "owner",
  Admin:  "admin",
} as const;
export type HelpRole = typeof HelpRole[keyof typeof HelpRole];

export interface HelpArticle {
  id: string;
  roles: HelpRole[];
  title: string;
  route?: string;
  keywords: string[];
  summary: string;
  steps: string[];
  tips?: string[];
  warnings?: string[];
  relatedArticleIds?: string[];
}

export interface FaqItem {
  id: string;
  roles: HelpRole[];
  question: string;
  answer: string;
  relatedArticleIds?: string[];
}

export interface HelpSearchResult {
  type: "article" | "faq";
  id: string;
  score: number;
  matchedOn: "title" | "keyword" | "body" | "question";
}
