/**
 * The archive page's helpers (Epic 8): byte sizes, the list's query and the browser-side check of the CSV before upload.
 */

/** The longest CSV the API accepts, in characters (about 1 MB). */
export const MAX_CSV_CHARACTERS = 1_000_000;

/** The folder states the list can be filtered by. */
export const ARCHIVE_STATES = ["new", "linked", "imported"] as const;

/** A folder state filter. */
export type ArchiveState = (typeof ARCHIVE_STATES)[number];

/** What the list shows. */
export type ArchiveQuery = { state: ArchiveState | null; page: number };

/** The decimal units, smallest first. */
const UNITS = ["B", "KB", "MB", "GB", "TB"];

/**
 * Writes a size in decimal units, one decimal above bytes.
 *
 * @param bytes - The size.
 * @returns The text, e.g. `4.0 GB`.
 */
export function formatBytes(bytes: number): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1000 && unit < UNITS.length - 1) {
    value /= 1000;
    unit++;
  }

  return unit === 0 ? `${value} B` : `${value.toFixed(1)} ${UNITS[unit]}`;
}

/**
 * Reads the list's query from the page's search params; unknown values fall back.
 *
 * @param raw - The search params.
 * @returns The state filter and the page.
 */
export function readArchiveQuery(raw: Record<string, string | string[] | undefined>): ArchiveQuery {
  const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
  const state = first(raw.state);
  const page = Number.parseInt(first(raw.page) ?? "", 10);
  return {
    state: (ARCHIVE_STATES as readonly string[]).includes(state ?? "") ? (state as ArchiveState) : null,
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

/**
 * Builds the address of a list view.
 *
 * @param query - The state filter and the page.
 * @returns The address, with only what is set.
 */
export function archiveHref(query: ArchiveQuery): string {
  const params = new URLSearchParams();
  if (query.state) {
    params.set("state", query.state);
  }

  if (query.page > 1) {
    params.set("page", String(query.page));
  }

  const search = params.toString();
  return search ? `/admin/archive?${search}` : "/admin/archive";
}

/**
 * Checks a CSV's text before it is sent (the API checks again).
 *
 * @param text - The file's text.
 * @returns Null when it can be sent; `empty` or `tooLarge` otherwise.
 */
export function checkCsvText(text: string): "empty" | "tooLarge" | null {
  if (!text.trim()) {
    return "empty";
  }

  return text.length > MAX_CSV_CHARACTERS ? "tooLarge" : null;
}
