/**
 * The players list keeps its search, filters and page in the address bar (A-3, decisions §2b), so a filtered view can
 * be bookmarked, shared and reloaded. Values from the address are checked; bad ones fall back to the defaults and never
 * reach the API.
 */

/** The position codes of the filter chips. */
export const PLAYER_POSITIONS = ["S", "OH", "OPP", "MB", "L", "DS"] as const;

/** The console statuses of the filter chips, as the API writes them in the query. */
export const PLAYER_STATUSES = ["live", "processing", "attention", "waiting"] as const;

/** The default page size of the list. */
export const DEFAULT_PAGE_SIZE = 25;

/** The largest page the API serves. */
const MAX_PAGE_SIZE = 100;

/** The longest search the API accepts. */
const MAX_SEARCH = 100;

/** What the players list shows. */
export type PlayersQuery = {
  q: string;
  position: string | null;
  status: string | null;
  page: number;
  pageSize: number;
};

/** The raw values of an address: what `URLSearchParams` or Next.js `searchParams` hold. */
export type RawQuery = URLSearchParams | Record<string, string | string[] | undefined>;

/**
 * Reads the first value of a parameter.
 *
 * @param raw - The address's parameters.
 * @param name - The parameter name.
 * @returns The value, or undefined.
 */
function first(raw: RawQuery, name: string): string | undefined {
  if (raw instanceof URLSearchParams) {
    return raw.get(name) ?? undefined;
  }

  const value = raw[name];
  return Array.isArray(value) ? value[0] : value;
}

/**
 * Reads a positive whole number.
 *
 * @param text - The text.
 * @param max - The largest accepted value.
 * @returns The number, or null when it is not one.
 */
function wholeNumber(text: string | undefined, max: number): number | null {
  if (!text || !/^\d+$/.test(text)) {
    return null;
  }

  const value = Number(text);
  return value >= 1 && value <= max ? value : null;
}

/**
 * Reads the list's state from the address.
 *
 * @param raw - The address's parameters.
 * @returns The state, with defaults for anything missing or bad.
 */
export function parsePlayersQuery(raw: RawQuery): PlayersQuery {
  const position = first(raw, "position");
  const status = first(raw, "status");
  return {
    q: (first(raw, "q") ?? "").trim().slice(0, MAX_SEARCH),
    position: (PLAYER_POSITIONS as readonly string[]).includes(position ?? "") ? (position as string) : null,
    status: (PLAYER_STATUSES as readonly string[]).includes(status ?? "") ? (status as string) : null,
    page: wholeNumber(first(raw, "page"), Number.MAX_SAFE_INTEGER) ?? 1,
    pageSize: wholeNumber(first(raw, "pageSize"), MAX_PAGE_SIZE) ?? DEFAULT_PAGE_SIZE,
  };
}

/**
 * Changes a filter; a changed filter always goes back to page 1.
 *
 * @param query - The current state.
 * @param patch - The filters to change; null clears one.
 * @returns The new state.
 */
export function withFilter(query: PlayersQuery, patch: Partial<Pick<PlayersQuery, "q" | "position" | "status">>): PlayersQuery {
  return { ...query, ...patch, page: 1 };
}

/**
 * Writes the filters as the API's query, in a stable order.
 *
 * @param query - The state.
 * @returns The filter parameters.
 */
function filterParams(query: PlayersQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.q) {
    params.set("q", query.q);
  }

  if (query.position) {
    params.set("position", query.position);
  }

  if (query.status) {
    params.set("status", query.status);
  }

  return params;
}

/**
 * Writes the list's address; defaults stay out of it.
 *
 * @param query - The state.
 * @returns The path with its query string.
 */
export function playersHref(query: PlayersQuery): string {
  const params = filterParams(query);
  if (query.page !== 1) {
    params.set("page", String(query.page));
  }

  if (query.pageSize !== DEFAULT_PAGE_SIZE) {
    params.set("pageSize", String(query.pageSize));
  }

  const text = params.toString();
  return text ? `/admin/players?${text}` : "/admin/players";
}

/**
 * Writes the API call that reads the list's page.
 *
 * @param query - The state.
 * @returns The API path with its query string.
 */
export function playersApiPath(query: PlayersQuery): string {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) });
  for (const [key, value] of filterParams(query)) {
    params.set(key, value);
  }

  return `/admin/players?${params.toString()}`;
}

/**
 * Writes the CSV export's address: the same filters, no paging, through the same-origin route.
 *
 * @param query - The state.
 * @returns The path with its query string.
 */
export function playersCsvHref(query: PlayersQuery): string {
  const text = filterParams(query).toString();
  return text ? `/api/admin/players.csv?${text}` : "/api/admin/players.csv";
}
