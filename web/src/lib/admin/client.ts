/**
 * Browser-side calls of the console: every action goes to the same-origin route `/api/admin/…`, which adds the session.
 */

/** The API refused an action. */
export class AdminActionError extends Error {
  /**
   * Creates the error.
   *
   * @param status - The HTTP status.
   * @param code - The ProblemDetails `code`, or null.
   */
  constructor(
    readonly status: number,
    readonly code: string | null,
  ) {
    super(`The action failed with ${status}${code ? ` (${code})` : ""}.`);
    this.name = "AdminActionError";
  }
}

/**
 * Calls an admin endpoint from the browser.
 *
 * @typeParam T - The answer's shape; `null` for endpoints that answer without a body.
 * @param method - The HTTP method.
 * @param path - The path after `/api/admin/`, with its query.
 * @param body - A JSON body, or undefined.
 * @returns The parsed answer; null when there is none.
 * @throws {AdminActionError} When the API refuses the action.
 */
export async function adminCall<T = null>(method: "GET" | "POST" | "PUT" | "PATCH", path: string, body?: unknown): Promise<T> {
  const answer = await fetch(`/api/admin/${path}`, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store",
  });
  if (!answer.ok) {
    let code: string | null = null;
    try {
      const problem = (await answer.json()) as { code?: unknown };
      code = typeof problem.code === "string" ? problem.code : null;
    } catch {
      code = null;
    }

    throw new AdminActionError(answer.status, code);
  }

  const text = await answer.text();
  return (text ? JSON.parse(text) : null) as T;
}

/**
 * Uploads a player's photo from the browser.
 *
 * @param playerId - The player.
 * @param file - The image.
 * @returns A task that completes when the photo is stored.
 * @throws {AdminActionError} When the API refuses the photo.
 */
export async function uploadPlayerPhoto(playerId: string, file: File): Promise<void> {
  const answer = await fetch(`/api/admin/players/${encodeURIComponent(playerId)}/photo`, { method: "PUT", headers: { "Content-Type": file.type }, body: file, cache: "no-store" });
  if (!answer.ok) {
    throw new AdminActionError(answer.status, null);
  }
}
