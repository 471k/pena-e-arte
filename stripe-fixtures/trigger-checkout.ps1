# Usage: .\stripe-fixtures\trigger-checkout.ps1 -StudioId <guid> -PriceId <stripe_price_id>
param(
    [Parameter(Mandatory)][string]$StudioId,
    [Parameter(Mandatory)][string]$PriceId
)

$env:STUDIO_ID = $StudioId
$env:PRICE_ID  = $PriceId

stripe fixtures "$PSScriptRoot\checkout-session-completed.json"
