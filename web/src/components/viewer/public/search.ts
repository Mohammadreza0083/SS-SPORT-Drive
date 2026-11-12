import type { PublicPlayer } from "./types";

/** The position chips, in the canvas order. */
export const POSITION_FILTERS = ["All", "S", "OH", "OPP", "MB", "L", "DS"] as const;

/**
 * Lower-cases, trims and strips accents so "José" matches "jose".
 *
 * @param value - The text.
 * @returns The comparable text.
 */
function fold(value: string): string {
  return value.normalize("NFD").replace(/\p{Diacritic}/gu, "").trim().toLowerCase();
}

/**
 * Filters players by name and position, keeping their order.
 *
 * @param players - Every player.
 * @param query - What the visitor typed; empty keeps everyone.
 * @param position - A position code, or "All".
 * @returns The matching players, as a new array.
 */
export function filterPlayers(players: readonly PublicPlayer[], query: string, position: string): PublicPlayer[] {
  const needle = fold(query);
  return players.filter((player) => (position === "All" || player.position === position) && (!needle || fold(player.name).includes(needle)));
}

/**
 * The live result count next to the search box.
 *
 * @param count - How many players are shown.
 * @returns For example `1 player` or `8 players`.
 */
export function resultCountLabel(count: number): string {
  return `${count} ${count === 1 ? "player" : "players"}`;
}

/**
 * The address of a home page state, so the search, the chips and the pager live in the URL (and the player QR code's
 * `/?q=<name>` keeps working).
 *
 * @param query - The search text; empty leaves `q` out.
 * @param position - A position code, or "All" which leaves `position` out.
 * @param page - The page number; 1 leaves `page` out.
 * @returns For example `/?q=tehr&position=OPP&page=2`, or `/`.
 */
export function homeHref(query: string, position: string, page: number): string {
  const params = new URLSearchParams();
  if (query.trim()) {
    params.set("q", query.trim());
  }

  if (position !== "All") {
    params.set("position", position);
  }

  if (page > 1) {
    params.set("page", String(page));
  }

  const text = params.toString();
  return text ? `/?${text}` : "/";
}
