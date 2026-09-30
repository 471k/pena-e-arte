export function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

const HTML_ENTITIES: Record<string, string> = {
  "&nbsp;": " ", "&amp;": "&", "&lt;": "<", "&gt;": ">", "&quot;": "\"", "&#39;": "'", "&apos;": "'",
};

/**
 * Plain-text preview of a stored email body. Notification bodies are full HTML documents, so
 * besides removing tags this drops the content of <head>/<title>/<style>/<script> entirely
 * (otherwise the <title> text shows up as the first words of the preview, duplicating the
 * heading) and decodes the common entities.
 */
export function stripHtml(html: string): string {
  return html
    .replace(/<(head|title|style|script)\b[^>]*>[\s\S]*?<\/\1>/gi, " ")
    .replace(/<[^>]*>/g, " ")
    .replace(/&(?:nbsp|amp|lt|gt|quot|apos|#39);/gi, (entity) => HTML_ENTITIES[entity.toLowerCase()] ?? entity)
    .replace(/\s+/g, " ")
    .trim();
}
