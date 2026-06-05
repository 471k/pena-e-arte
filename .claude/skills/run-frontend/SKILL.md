# run-frontend

Start and stop the Vite dev server for the React frontend.

## Prerequisites

- Node / pnpm installed at `C:\nvm4w\nodejs\pnpm.cmd`
- Working directory for all commands: `frontend\` (relative to repo root)
- Backend optional — Vite proxies `/api` and `/hubs` to `http://localhost:5078`

## Start

```powershell
# From repo root
Set-Location "frontend"
Start-Process -NoNewWindow -FilePath "C:\nvm4w\nodejs\pnpm.cmd" `
  -ArgumentList "dev","--port","5173"
```

Poll until ready (do not sleep a fixed amount):

```powershell
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
  try {
    Invoke-WebRequest -Uri "http://localhost:5173" -UseBasicParsing -TimeoutSec 2 | Out-Null
    "ready"; break
  } catch { Start-Sleep -Milliseconds 500 }
}
```

## Verify it's up

```powershell
Invoke-WebRequest -Uri "http://localhost:5173" -UseBasicParsing -TimeoutSec 5 | Select-Object -ExpandProperty StatusCode
# → 200
```

## Stop

```powershell
# Kill the node processes that were started since the dev server began.
# Using port to target precisely:
$pids = (Get-NetTCPConnection -LocalPort 5173 -ErrorAction SilentlyContinue).OwningProcess
if ($pids) { Stop-Process -Id $pids -Force -Confirm:$false }
```

## Notes

- Port 5173 is the fixed port. If it's already in use a prior dev server is still running — stop it first.
- First page load after a cold start compiles routes on demand; `wait-for` the element in Playwright rather than sleeping.
- The backend is not required for frontend rendering. API calls will fail with a network error when it's down, which exercises the error-state UI.
