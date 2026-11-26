import type { LegalClause } from "./legalTypes";

// Renders a list of legal clauses with the same typography the other public pages use.
export function LegalClauses({ clauses }: { clauses: ReadonlyArray<LegalClause> }) {
  return (
    <>
      {clauses.map((clause: LegalClause) => (
        <section key={clause.heading} className="mt-6">
          <h2 className="text-lg font-semibold tracking-tight">{clause.heading}</h2>
          <div className="mt-2 space-y-2 text-sm text-muted-foreground">
            {clause.paragraphs?.map((text: string) => <p key={text}>{text}</p>)}
            {clause.bullets && (
              <ul className="list-disc space-y-1 pl-5">
                {clause.bullets.map((item: string) => <li key={item}>{item}</li>)}
              </ul>
            )}
          </div>
        </section>
      ))}
    </>
  );
}

export function LegalVersionLine({ lastUpdated, version }: { lastUpdated: string; version: string }) {
  return (
    <p className="mt-1 text-xs text-muted-foreground">
      Last updated: {lastUpdated}. Version {version}.
    </p>
  );
}
