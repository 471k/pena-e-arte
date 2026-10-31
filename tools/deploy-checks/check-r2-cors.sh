#!/usr/bin/env bash
# Checks that browsers on the given origins may upload to the R2 bucket.
#
# Photo uploads PUT straight from the browser to R2 with a presigned URL, so the bucket's CORS
# policy must allow the app's origin. Production's bucket had no policy until 27 Oct 2026: every
# browser upload failed its preflight (403), which blocked guest booking (photos are required).
# A preflight needs no signature, so this uses no keys; it never prints the account id or bucket.
#
# Usage: check-r2-cors.sh <account-id> <bucket> <origin> [<origin> ...]
set -euo pipefail

account="$1"; bucket="$2"; shift 2
url="https://${account}.r2.cloudflarestorage.com/${bucket}/cors-probe"
failed=0

for origin in "$@"; do
  headers=$(curl -sS -o /dev/null -D - --max-time 15 -X OPTIONS "$url" \
    -H "Origin: ${origin}" \
    -H "Access-Control-Request-Method: PUT" \
    -H "Access-Control-Request-Headers: content-type" | tr -d '\r')
  status=$(printf '%s\n' "$headers" | head -n1 | awk '{print $2}')
  allowed=$(printf '%s\n' "$headers" | grep -i '^access-control-allow-origin:' | cut -d' ' -f2- || true)
  if [[ "$status" == 2* ]] && { [ "$allowed" = "$origin" ] || [ "$allowed" = "*" ]; }; then
    echo "OK    ${origin} may upload (HTTP ${status})"
  else
    echo "FAIL  ${origin} may NOT upload (HTTP ${status:-none}, Access-Control-Allow-Origin '${allowed}')"
    echo "      Fix: Cloudflare dashboard > R2 > the bucket > Settings > CORS policy (allow GET, PUT, HEAD from this origin)."
    failed=1
  fi
done

exit "$failed"
