/**
 * The access-request inbox (A-12, canvas `AdminRequests`) keeps its tab, page and open request in the address bar, so a
 * view can be reloaded and shared. Values from the address are checked; bad ones fall back to the defaults and never
 * reach the API. The approval body follows `POST /admin/access-requests/{id}/approve` (P-4, decisions §7).
 */

/** The tabs of the inbox, as the API's `status` filter writes them. */
export const REQUEST_STATUSES = ["pending", "approved", "declined"] as const;

/** One tab of the inbox. */
export type RequestStatus = (typeof REQUEST_STATUSES)[number];

/** What the inbox shows. */
export type RequestsQuery = { status: RequestStatus; page: number; id: string | null };

/** The most players one approval may grant (the API's limit). */
export const MAX_GRANTED_PLAYERS = 20;

/** A GUID as the API writes it. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Reads the first value of a parameter.
 *
 * @param raw - Next.js `searchParams`.
 * @param name - The parameter name.
 * @returns The value, or undefined.
 */
function first(raw: Record<string, string | string[] | undefined>, name: string): string | undefined {
  const value = raw[name];
  return Array.isArray(value) ? value[0] : value;
}

/**
 * Reads the inbox's view from the address.
 *
 * @param raw - Next.js `searchParams`.
 * @returns The tab (pending by default), the page (at least 1) and the open request (a GUID, or null).
 */
export function readRequestsQuery(raw: Record<string, string | string[] | undefined>): RequestsQuery {
  const status = (first(raw, "status") ?? "").toLowerCase();
  const page = first(raw, "page") ?? "";
  const id = first(raw, "id") ?? "";
  return {
    status: (REQUEST_STATUSES as readonly string[]).includes(status) ? (status as RequestStatus) : "pending",
    page: /^\d+$/.test(page) && Number(page) >= 1 ? Number(page) : 1,
    id: GUID.test(id) ? id : null,
  };
}

/**
 * Writes the inbox's address for a view, leaving the defaults out.
 *
 * @param query - The view.
 * @returns The address.
 */
export function requestsHref(query: RequestsQuery): string {
  const params = new URLSearchParams();
  if (query.status !== "pending") {
    params.set("status", query.status);
  }

  if (query.page > 1) {
    params.set("page", String(query.page));
  }

  if (query.id) {
    params.set("id", query.id);
  }

  const text = params.toString();
  return text ? `/admin/requests?${text}` : "/admin/requests";
}

/** The body of `POST /admin/access-requests/{id}/approve`. */
export type ApprovalBody = { expiresInDays: number; playerIds: string[]; note?: string };

/**
 * Builds the approval body from the admin's choices: the players without repeats (at most twenty), the expiry and the
 * note when it says something.
 *
 * @param choices - The players to grant, the expiry in days and the note.
 * @param choices.playerIds - The players to grant.
 * @param choices.expiresInDays - 7, 30 or 90.
 * @param choices.note - The note for the email, as typed.
 * @returns The body.
 */
export function buildApproval({ playerIds, expiresInDays, note }: { playerIds: string[]; expiresInDays: number; note: string }): ApprovalBody {
  const body: ApprovalBody = { expiresInDays, playerIds: [...new Set(playerIds)].slice(0, MAX_GRANTED_PLAYERS) };
  const text = note.trim();
  if (text) {
    body.note = text;
  }

  return body;
}

/**
 * The two letters of a requester's avatar: the first letters and digits of the address's name part.
 *
 * @param email - The verified email.
 * @returns One or two capital letters.
 */
export function requesterInitials(email: string): string {
  const name = email.split("@")[0] ?? "";
  const parts = name.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : (parts[0] ?? "").slice(0, 2);
  return letters.toUpperCase();
}
