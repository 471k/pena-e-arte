/** Fetches an API endpoint with the same auth headers RTK Query's baseQuery attaches
 *  (Authorization + X-Tenant-Id), then triggers a browser download of the response body
 *  under the given filename. For endpoints that return a file (CSV, etc.) rather than
 *  JSON, which RTK Query isn't a natural fit for.
 *
 *  Takes the caller's token/tenantId as arguments instead of reading the Redux store
 *  directly — this repo's convention (conventions.md) is "no direct store.getState()
 *  outside Redux middleware"; a component calling this from a click handler should read
 *  them via useAppSelector and pass them in. */
export async function downloadAuthenticatedFile(
  path: string,
  filename: string,
  token: string | null,
  tenantId: string | null,
): Promise<void> {
  const headers: Record<string, string> = {};
  if (token)    headers.Authorization = `Bearer ${token}`;
  if (tenantId) headers["X-Tenant-Id"] = tenantId;

  const response = await fetch(`/api/v1/${path}`, { headers });
  if (!response.ok) throw new Error(`Download failed (${response.status})`);

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
