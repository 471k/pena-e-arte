# Design Sync Notes — Pena e Arte

## Re-sync command

```bash
node .ds-sync/resync.mjs \
  --config .design-sync/config.json \
  --node-modules ./node_modules \
  --entry src/shared/components/index.ts \
  --out ./ds-bundle \
  --remote .design-sync/.cache/remote-sync.json
```

First sync (no remote anchor): omit `--remote`.

## CSS entry hash

`config.json` has `"cssEntry": "dist/assets/index-DgR6B_YM.css"`. That hash changes when Vite rebuilds. After any `pnpm build`, check `dist/assets/` and update `cssEntry` in config if the hash changed before re-syncing.

## Redux-dependent components are excluded

The following components import from `@/app/hooks` or `@/features/...` and cannot be type-checked in isolation:

- `UserChip.tsx`
- `ReadOnlyBanner.tsx`
- `SuspensionBanner.tsx`
- `SubscriptionGatedButton.tsx` (skipped in config)
- `FileUploadField.tsx` (skipped in config)

They are excluded from `src/shared/components/index.ts` (the barrel entry) and from `tsconfig.ds.json`'s `exclude` list. Adding new Redux-dependent components to the shared folder requires the same treatment.

## Declaration file generation (`tsconfig.ds.json`)

The converter reads `.d.ts` files, not TypeScript source. This app doesn't have a `dist/types` directory by default (Vite doesn't emit declarations). The workaround:

```bash
npx tsc -p tsconfig.ds.json
```

This emits declaration files to `dist/types/shared/components/`. The `"types"` field in `package.json` points the converter at the right entry: `"dist/types/shared/components/index.d.ts"`.

**If tsc produces stale output at `dist/types/src/...`**: delete `dist/types` and re-run. This happens if `rootDir` wasn't set on a prior run.

After a pnpm build, re-run `tsc -p tsconfig.ds.json` before the driver run so declarations stay current.

## Playwright for preview screenshots

The `.ds-sync/` scripts require the `playwright` package (not `@playwright/test`). Install once per machine:

```bash
npm --prefix .ds-sync install playwright
.ds-sync\node_modules\.bin\playwright install chromium
```

## Authored previews

Three components have hand-authored previews in `.design-sync/previews/` because the auto-generated floor cards were blank or near-invisible:

- **`Avatar.tsx`** — must include `AvatarFallback` as a child; the `Avatar` root renders as an invisible container otherwise.
- **`DataTable.tsx`** — needs typed `columns` + `data` arrays; empty table renders nothing visible.
- **`Input.tsx`** — needs a wrapper `<div style={{ padding: '8px' }}>` so the screenshot exceeds the 5 KB blank threshold in dark mode at preview scale.

These are in `.design-sync/previews/` and are picked up automatically by the converter (`(preview override: <Name)` in the build log).
