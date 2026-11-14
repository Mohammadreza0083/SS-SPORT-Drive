/**
 * Small helpers for showing people in the console.
 */

/**
 * Makes the initials of a name or an email for an avatar.
 *
 * @param name - A display name or an email.
 * @returns One or two upper-case letters, or `?` for nothing.
 */
export function formatInitials(name: string): string {
  const words = name
    .split("@")[0]
    .split(/[\s._-]+/)
    .filter(Boolean);
  const letters = words.length > 1 ? `${words[0][0]}${words[1][0]}` : (words[0] ?? "").slice(0, 2);
  return letters ? letters.toUpperCase() : "?";
}

/**
 * Writes a time of day or date for a table cell, in Tehran.
 *
 * @param at - The time (ISO 8601), or null.
 * @returns For example `14 Nov, 12:30`, or a dash.
 */
export function formatDateTime(at: string | null): string {
  if (!at) {
    return "—";
  }

  return new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: "Asia/Tehran" }).format(new Date(at));
}

/**
 * Writes a date for a table cell, in Tehran.
 *
 * @param at - The time (ISO 8601), or null.
 * @returns For example `30 Nov`, or a dash.
 */
export function formatDate(at: string | null): string {
  if (!at) {
    return "—";
  }

  return new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: "Asia/Tehran" }).format(new Date(at));
}
