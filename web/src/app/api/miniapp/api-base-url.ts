/**
 * Where the Next.js server reaches the API, read at request time so Docker images need no rebuild.
 *
 * @returns `API_BASE_URL`, or the local API when it is not set.
 */
export function apiBaseUrl(): string {
  return process.env.API_BASE_URL ?? "http://localhost:5080";
}
