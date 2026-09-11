export function extractErrorMessage(err: unknown, fallback: string): string {
  return err && typeof err === "object" && "data" in err && err.data &&
    typeof err.data === "object" && "message" in err.data
    ? String((err.data as { message: string }).message)
    : fallback;
}
