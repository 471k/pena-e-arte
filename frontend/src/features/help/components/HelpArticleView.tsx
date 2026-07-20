import { ArrowLeft, ExternalLink, AlertTriangle, Lightbulb } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Alert, AlertDescription } from "@/shared/components/ui/alert";
import { HELP_ARTICLES } from "../helpContent";
import type { HelpArticle } from "../help.types";

interface HelpArticleViewProps {
  article: HelpArticle;
  onBack: () => void;
  onGoToPage: (route: string) => void;
  onSelectRelated: (id: string) => void;
}

export function HelpArticleView({ article, onBack, onGoToPage, onSelectRelated }: HelpArticleViewProps) {
  const related = (article.relatedArticleIds ?? [])
    .map((id) => HELP_ARTICLES.find((a) => a.id === id))
    .filter((a): a is HelpArticle => a !== undefined);

  return (
    <div className="flex-1 overflow-y-auto space-y-4">
      <Button variant="ghost" size="sm" onClick={onBack} className="-ml-2">
        <ArrowLeft className="h-4 w-4 mr-1.5" />
        Back
      </Button>

      <div className="space-y-1.5">
        <h3 className="text-base font-semibold">{article.title}</h3>
        <p className="text-sm text-muted-foreground">{article.summary}</p>
      </div>

      {article.route && (
        <Button size="sm" onClick={() => onGoToPage(article.route!)}>
          Go to this page
          <ExternalLink className="h-3.5 w-3.5 ml-1.5" />
        </Button>
      )}

      <ol className="space-y-2 list-decimal list-inside text-sm">
        {article.steps.map((step, i) => (
          <li key={i} className="pl-1">{step}</li>
        ))}
      </ol>

      {article.tips && article.tips.length > 0 && (
        <Alert>
          <Lightbulb className="h-4 w-4" />
          <AlertDescription>
            <ul className="space-y-1">
              {article.tips.map((tip, i) => <li key={i}>{tip}</li>)}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {article.warnings && article.warnings.length > 0 && (
        <Alert variant="destructive" role="note">
          <AlertTriangle className="h-4 w-4" />
          <AlertDescription>
            <ul className="space-y-1">
              {article.warnings.map((warning, i) => <li key={i}>{warning}</li>)}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {related.length > 0 && (
        <div className="space-y-1.5 pt-2 border-t">
          <p className="text-xs font-medium text-muted-foreground">Related</p>
          {related.map((r) => (
            <button
              key={r.id}
              type="button"
              onClick={() => onSelectRelated(r.id)}
              className="block w-full text-left text-sm text-primary hover:underline"
            >
              {r.title}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
