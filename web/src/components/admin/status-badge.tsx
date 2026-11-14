import { StatusPill, type PillTone } from "./shell/page-header";

/** The tone of each status or state word the console shows. */
const TONES: Record<string, PillTone> = {
  Live: "green",
  Processing: "blue",
  Attention: "amber",
  Waiting: "grey",
  Active: "green",
  Open: "green",
  Ready: "green",
  Sent: "green",
  SignedIn: "green",
  Queued: "grey",
  Copying: "blue",
  Paused: "amber",
  Pending: "amber",
  Expired: "grey",
  Revoked: "red",
  Failed: "red",
  Declined: "grey",
  Removed: "grey",
  Hidden: "grey",
  Skipped: "grey",
  LinkOnly: "blue",
  NotYet: "grey",
};

/**
 * A status or processing state as a coloured pill.
 *
 * @param props - The badge's props.
 * @param props.status - The status word, as the API writes it.
 * @param props.label - The text to show instead of the word.
 * @returns The pill.
 */
export function StatusBadge({ status, label }: { status: string; label?: string }) {
  return <StatusPill tone={TONES[status] ?? "grey"}>{label ?? status}</StatusPill>;
}
