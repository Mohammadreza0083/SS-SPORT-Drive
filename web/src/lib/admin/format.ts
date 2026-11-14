/**
 * How the console writes times and numbers, as the design canvas does: watch time `41m 10s`, relative times
 * `now`, `2 min ago`, `1 h ago`, `Yesterday`, `3 days ago`, then a short date `12 Oct`; completion as a whole percent.
 */

/** The time zone the console shows dates in. */
const TEHRAN = "Asia/Tehran";

/** The short date format of times older than a week. */
const SHORT_DATE = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: TEHRAN });

/**
 * Writes a length of watching.
 *
 * @param seconds - The seconds watched.
 * @returns `0m`, `0m 55s`, `41m 10s` or `1h 48m`.
 */
export function formatWatchTime(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  if (total === 0) {
    return "0m";
  }

  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m ${total % 60}s`;
}

/**
 * Writes how long ago something happened.
 *
 * @param at - The time (ISO 8601), or null.
 * @param now - The current time.
 * @returns `now`, `2 min ago`, `1 h ago`, `Yesterday`, `3 days ago`, a short date, or a dash for no time.
 */
export function formatRelative(at: string | null, now: Date): string {
  if (!at) {
    return "—";
  }

  const seconds = Math.max(0, (now.getTime() - new Date(at).getTime()) / 1000);
  if (seconds < 60) {
    return "now";
  }

  if (seconds < 3600) {
    return `${Math.floor(seconds / 60)} min ago`;
  }

  if (seconds < 86_400) {
    return `${Math.floor(seconds / 3600)} h ago`;
  }

  if (seconds < 172_800) {
    return "Yesterday";
  }

  if (seconds < 7 * 86_400) {
    return `${Math.floor(seconds / 86_400)} days ago`;
  }

  return SHORT_DATE.format(new Date(at));
}

/**
 * Writes a share as a whole percent.
 *
 * @param ratio - A share from 0 to 1, or null for no data.
 * @returns `72%`, or a dash.
 */
export function formatPercent(ratio: number | null): string {
  return ratio === null ? "—" : `${Math.round(ratio * 100)}%`;
}

/**
 * Counts the whole days left on a session.
 *
 * @param expiresAt - When the session ends (ISO 8601).
 * @param now - The current time.
 * @returns The whole days left, never below 0.
 */
export function sessionDaysLeft(expiresAt: string, now: Date): number {
  return Math.max(0, Math.floor((new Date(expiresAt).getTime() - now.getTime()) / 86_400_000));
}
