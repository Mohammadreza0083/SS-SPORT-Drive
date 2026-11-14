import type { Audience } from "@/lib/auth/sign-in-server";

/** What asking for a sign-in link came to. */
export type SignInResult =
  | { kind: "sent" }
  | { kind: "too-many"; retryAfterSeconds: number | null }
  | { kind: "invalid" }
  | { kind: "failed" };

/** What the sign-in panel needs from the server (a test passes a fake). */
export type SignInClient = {
  /**
   * Asks for a sign-in link to be emailed.
   *
   * @param email - The address.
   * @returns What happened.
   */
  requestLink(email: string): Promise<SignInResult>;
};

/**
 * Creates the browser client of the sign-in page: it calls the same-origin `/api/auth/{audience}/link` route with JSON.
 * The API answers 202 for every well-formed email, so the answer says nothing about who has an account.
 *
 * @param audience - Who signs in.
 * @param fetchImpl - The fetch to use (tests pass a stub).
 * @returns The client.
 */
export function createSignInClient(audience: Audience, fetchImpl: typeof fetch = fetch): SignInClient {
  return {
    async requestLink(email) {
      let response: Response;
      try {
        response = await fetchImpl(`/api/auth/${audience}/link`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email }),
        });
      } catch {
        return { kind: "failed" };
      }

      if (response.status === 202) {
        return { kind: "sent" };
      }

      if (response.status === 429) {
        const wait = Number.parseInt(response.headers.get("Retry-After") ?? "", 10);
        return { kind: "too-many", retryAfterSeconds: Number.isFinite(wait) ? wait : null };
      }

      return response.status === 400 || response.status === 422 ? { kind: "invalid" } : { kind: "failed" };
    },
  };
}
