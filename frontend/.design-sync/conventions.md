# TattooOS UI — Design Conventions

## Wrapping and setup

No provider or context wrapper is required. Components render standalone with Tailwind CSS v4 utility classes and CSS custom properties defined in `styles.css`. Import everything from `'tattoos-ui'`:

```tsx
import { Button, Card, CardHeader, CardContent } from 'tattoos-ui';
```

Dark mode responds automatically to `@media (prefers-color-scheme: dark)` — no class toggle needed.

## Styling idiom: Tailwind CSS v4 utility classes

Style all layout and glue code with Tailwind utilities. The token vocabulary maps directly to `--color-*` CSS custom properties via `@theme` blocks in `styles.css`. Never use inline styles for brand colors — use the classes below.

**Color classes** (apply `bg-`, `text-`, or `border-` prefix):
| Token | Purpose |
|---|---|
| `background` / `foreground` | Page/canvas bg and default text |
| `card` / `card-foreground` | Card surfaces |
| `primary` / `primary-foreground` | Primary action color (near-black light, near-white dark) |
| `secondary` / `secondary-foreground` | Subtle fills |
| `muted` / `muted-foreground` | De-emphasized text and backgrounds |
| `accent` / `accent-foreground` | Hover/active highlight |
| `destructive` / `destructive-foreground` | Danger/delete actions |
| `input` | Input border color |
| `ring` | Focus ring color |

Examples: `bg-background text-foreground`, `bg-primary text-primary-foreground`, `text-muted-foreground`.

**Border and radius**: `border border-input`, `rounded-md` (0.5rem), `rounded-full`, `rounded-sm`.

**Font**: system-ui / -apple-system — no brand font to import.

## Compound component composition

Several components are compound and must be composed with their sub-parts:

- **Card**: `<Card><CardHeader><CardTitle>…</CardTitle></CardHeader><CardContent>…</CardContent></Card>`
- **Avatar**: `<Avatar><AvatarFallback>JD</AvatarFallback></Avatar>` — always include `AvatarFallback`; `AvatarImage` is optional
- **Dialog**: `<Dialog open={true}><DialogContent><DialogHeader><DialogTitle>…</DialogTitle></DialogHeader>…</DialogContent></Dialog>`
- **Select**: `<Select><SelectTrigger><SelectValue placeholder="Pick…"/></SelectTrigger><SelectContent><SelectItem value="x">X</SelectItem></SelectContent></Select>`
- **Tabs**: `<Tabs defaultValue="a"><TabsList><TabsTrigger value="a">A</TabsTrigger></TabsList><TabsContent value="a">…</TabsContent></Tabs>`
- **Table**: `<Table><TableHeader><TableRow><TableHead>Col</TableHead></TableRow></TableHeader><TableBody><TableRow><TableCell>…</TableCell></TableRow></TableBody></Table>`
- **DataTable**: higher-level wrapper — pass `columns` (array of `{header, accessorKey?}`) and `data` array with `keyExtractor`

## Idiomatic build snippet

```tsx
import { Card, CardContent, CardHeader, CardTitle, Button, Badge } from 'tattoos-ui';

export function AppointmentCard() {
  return (
    <Card className="w-80">
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          Upcoming Session
          <Badge variant="secondary">Confirmed</Badge>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        <p className="text-sm text-muted-foreground">Alice Martin · 2h tattoo session</p>
        <Button size="sm" variant="outline">View details</Button>
      </CardContent>
    </Card>
  );
}
```

## Where the truth lives

- Token definitions: `styles.css` → `_ds_bundle.css` (the `@theme` block with all `--color-*` variables)
- Per-component API: `components/<group>/<Name>/<Name>.d.ts` and `<Name>.prompt.md`
- Read `styles.css` and the component's `.d.ts` before styling — the class vocabulary above is complete; do not invent `--color-*` names not listed here
