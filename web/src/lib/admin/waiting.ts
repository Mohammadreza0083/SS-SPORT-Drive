/**
 * The waiting-list drawer's checks (A-5), the same rules as the API's `Player.MissingDetails()`: a full name, a
 * nationality, one or two positions, a Volleybox player link ending in `-p<id>` and a photo.
 */

/** The most positions a player has. */
const MAX_POSITIONS = 2;

/** A Volleybox player link: the host, a slug and `-p` with the id. */
const VOLLEYBOX = /(?:^|\/\/|\.)volleybox\.net\/[a-z0-9-]+-p(\d+)(?:[/?#]|$)/i;

/** What the drawer edits. */
export type WaitingDetails = {
  name: string;
  nationalityCode: string | null;
  positions: string[];
  volleyboxUrl: string;
  hasPhoto: boolean;
};

/**
 * Reads the player id from a Volleybox link.
 *
 * @param url - The link as typed or pasted.
 * @returns The id, or null when the link is not a player's.
 */
export function volleyboxId(url: string): number | null {
  const match = VOLLEYBOX.exec(url.trim());
  return match ? Number(match[1]) : null;
}

/**
 * Lists the details still missing, in the order the API lists them.
 *
 * @param details - What the drawer holds.
 * @returns `Name`, `Nationality`, `Positions`, `Volleybox` and `Photo`, as far as they are missing.
 */
export function missingDetails(details: WaitingDetails): string[] {
  const missing: string[] = [];
  if (!details.name.trim()) {
    missing.push("Name");
  }

  if (!details.nationalityCode) {
    missing.push("Nationality");
  }

  if (details.positions.length === 0 || details.positions.length > MAX_POSITIONS) {
    missing.push("Positions");
  }

  if (volleyboxId(details.volleyboxUrl) === null) {
    missing.push("Volleybox");
  }

  if (!details.hasPhoto) {
    missing.push("Photo");
  }

  return missing;
}
