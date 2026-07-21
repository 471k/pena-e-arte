import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { HelpCircle, ChevronRight } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/shared/components/ui/sheet";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/shared/components/ui/tabs";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { useAppSelector } from "@/app/hooks";
import { HELP_ARTICLES, FAQ_ITEMS } from "../helpContent";
import { searchHelp } from "../helpSearch";
import { useLogHelpSearchMutation } from "../helpApi";
import { useOnboardingTour } from "../useOnboardingTour";
import { HelpSearchInput } from "./HelpSearchInput";
import { HelpArticleView } from "./HelpArticleView";
import { FaqAccordion } from "./FaqAccordion";
import { ContactSupportPanel } from "./ContactSupportPanel";
import type { HelpArticle, FaqItem, HelpRole, HelpSearchResult } from "../help.types";
import type { Role } from "@/shared/types/roles";

const SEARCH_LOG_DEBOUNCE_MS = 800;

export function HelpMenu() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [selectedArticleId, setSelectedArticleId] = useState<string | null>(null);
  const [showAllRoles, setShowAllRoles] = useState(false);
  const role = useAppSelector((s) => s.auth.role) as HelpRole | null;
  const navigate = useNavigate();
  const [logHelpSearch] = useLogHelpSearchMutation();
  const loggedQueriesRef = useRef<Set<string>>(new Set());
  const { tourElement, restartTour } = useOnboardingTour(role as Role | null);

  const scopedArticles = useMemo(() => {
    if (!role) return [];
    if (role === "issuer" && showAllRoles) return HELP_ARTICLES;
    return HELP_ARTICLES.filter((a) => a.roles.includes(role));
  }, [role, showAllRoles]);

  const scopedFaqs = useMemo(() => {
    if (!role) return [];
    return FAQ_ITEMS.filter((f) => f.roles.includes(role));
  }, [role]);

  const results = useMemo(
    () => searchHelp(query, scopedArticles, scopedFaqs),
    [query, scopedArticles, scopedFaqs],
  );

  const selectedArticle = scopedArticles.find((a) => a.id === selectedArticleId) ?? null;

  function handleGoToPage(route: string) {
    setOpen(false);
    navigate(route);
  }

  function handleClose(next: boolean) {
    setOpen(next);
    if (!next) {
      setQuery("");
      setSelectedArticleId(null);
      loggedQueriesRef.current.clear();
    }
  }

  // Debounce the search-analytics log call so it never fires on every keystroke —
  // the visible search above stays instant, only this side-channel call is delayed.
  useEffect(() => {
    if (query.trim().length < 2) return;
    const timer = setTimeout(() => {
      const normalized = query.trim().toLowerCase();
      if (loggedQueriesRef.current.has(normalized)) return;
      loggedQueriesRef.current.add(normalized);
      logHelpSearch({ query: normalized, resultCount: results.length })
        .unwrap()
        .catch(() => {});
    }, SEARCH_LOG_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [query, results.length, logHelpSearch]);

  useEffect(() => {
    function handler(e: KeyboardEvent) {
      const target = e.target as HTMLElement;
      const isTyping = ["INPUT", "TEXTAREA"].includes(target.tagName);
      if (e.key === "?" && e.shiftKey && !isTyping) {
        e.preventDefault();
        setOpen(true);
      }
    }
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  return (
    <>
      <Button
        variant="ghost" size="icon" className="h-8 w-8"
        onClick={() => setOpen(true)}
        title="Help" aria-label="Open help menu"
        data-tour={role ? `${role}-help-button` : undefined}
      >
        <HelpCircle className="h-4 w-4" />
      </Button>

      {tourElement}

      <Sheet open={open} onOpenChange={handleClose}>
        <SheetContent side="right" className="w-full sm:max-w-md flex flex-col">
          <SheetHeader>
            <SheetTitle>Help</SheetTitle>
          </SheetHeader>

          <HelpSearchInput value={query} onChange={setQuery} autoFocus />

          {query.length >= 2 ? (
            <SearchResultsList
              results={results}
              query={query}
              articles={scopedArticles}
              faqs={scopedFaqs}
              onSelectArticle={(id) => { setSelectedArticleId(id); setQuery(""); }}
              onClearQuery={() => setQuery("")}
            />
          ) : selectedArticle ? (
            <HelpArticleView
              article={selectedArticle}
              onBack={() => setSelectedArticleId(null)}
              onGoToPage={handleGoToPage}
              onSelectRelated={setSelectedArticleId}
            />
          ) : (
            <Tabs defaultValue="guides" className="flex-1 flex flex-col overflow-hidden">
              <TabsList>
                <TabsTrigger value="guides">Guides</TabsTrigger>
                <TabsTrigger value="faq">FAQ</TabsTrigger>
                <TabsTrigger value="support">Contact Support</TabsTrigger>
              </TabsList>
              <TabsContent value="guides" className="flex-1 overflow-y-auto space-y-3">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => { setOpen(false); restartTour(); }}
                >
                  Take the tour again
                </Button>
                {role === "issuer" && (
                  <IssuerAllRolesToggle checked={showAllRoles} onChange={setShowAllRoles} />
                )}
                <GuideList articles={scopedArticles} onSelect={setSelectedArticleId} />
              </TabsContent>
              <TabsContent value="faq" className="flex-1 overflow-y-auto">
                <FaqAccordion items={scopedFaqs} />
              </TabsContent>
              <TabsContent value="support" className="flex-1 overflow-y-auto">
                <ContactSupportPanel />
              </TabsContent>
            </Tabs>
          )}
        </SheetContent>
      </Sheet>
    </>
  );
}

interface SearchResultsListProps {
  results: HelpSearchResult[];
  query: string;
  articles: HelpArticle[];
  faqs: FaqItem[];
  onSelectArticle: (id: string) => void;
  onClearQuery: () => void;
}

function SearchResultsList({ results, query, articles, faqs, onSelectArticle, onClearQuery }: SearchResultsListProps) {
  if (results.length === 0) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center gap-2 py-8 text-center">
        <p className="text-sm text-muted-foreground">
          No results for &quot;{query}&quot;. Try a different word, or browse Guides and FAQ below.
        </p>
        <Button variant="outline" size="sm" onClick={onClearQuery}>Clear search</Button>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto space-y-1 pt-2">
      {results.map((r) => {
        if (r.type === "article") {
          const article = articles.find((a) => a.id === r.id);
          if (!article) return null;
          return (
            <button
              key={`article-${r.id}`}
              type="button"
              onClick={() => onSelectArticle(r.id)}
              className="w-full flex items-center justify-between gap-2 rounded-md px-3 py-2.5 text-left text-sm hover:bg-muted transition-colors"
            >
              <div>
                <p className="font-medium">{article.title}</p>
                <p className="text-xs text-muted-foreground line-clamp-1">{article.summary}</p>
              </div>
              <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
            </button>
          );
        }
        const faq = faqs.find((f) => f.id === r.id);
        if (!faq) return null;
        return (
          <div key={`faq-${r.id}`} className="rounded-md px-3 py-2.5 text-sm">
            <p className="font-medium">{faq.question}</p>
            <p className="text-xs text-muted-foreground mt-0.5">{faq.answer}</p>
          </div>
        );
      })}
    </div>
  );
}

function GuideList({ articles, onSelect }: { articles: HelpArticle[]; onSelect: (id: string) => void }) {
  if (articles.length === 0) {
    return <p className="text-sm text-muted-foreground py-4">No guides available yet.</p>;
  }

  return (
    <div className="space-y-1">
      {articles.map((article) => (
        <button
          key={article.id}
          type="button"
          onClick={() => onSelect(article.id)}
          className="w-full flex items-center justify-between gap-2 rounded-md px-3 py-2.5 text-left text-sm hover:bg-muted transition-colors"
        >
          <span className="font-medium">{article.title}</span>
          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        </button>
      ))}
    </div>
  );
}

function IssuerAllRolesToggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <Alert>
      <AlertDescription className="flex items-center justify-between gap-3">
        <span>
          As a platform admin you can also open every Client, Artist, and Owner screen for
          support purposes. Toggle below to search their guides too.
        </span>
      </AlertDescription>
      <Button
        type="button"
        variant={checked ? "default" : "outline"}
        size="sm"
        className="col-start-2 mt-2"
        onClick={() => onChange(!checked)}
      >
        {checked ? "Showing all roles' guides" : "Show all roles' guides"}
      </Button>
    </Alert>
  );
}
