/**
 * DataVolley scout reports on the player page (backlog V-8): the shape the API answers and how it is put into words.
 * The file itself never reaches the browser; the API parses it and answers the match and this player's stats per skill.
 */

/** One set's final score. */
export type ScoutSet = { home: number; away: number };

/** The match header of a report. */
export type ScoutMatch = {
  /** The match date, `YYYY-MM-DD`, or null when the file has none. */
  date: string | null;
  homeTeam: string;
  awayTeam: string;
  homeSets: number;
  awaySets: number;
  /** The final score of each played set. */
  sets: ScoutSet[];
};

/** One skill's numbers for the player. */
export type ScoutSkillStats = {
  /** Serve, Reception, Attack, Block, Dig or Set. */
  skill: string;
  total: number;
  perfect: number;
  positive: number;
  ok: number;
  negative: number;
  poor: number;
  error: number;
  /** Whole percent, or null without actions. */
  successRate: number | null;
  /** Whole percent (can be negative), or null without actions. */
  efficiency: number | null;
};

/** A parsed scout file for the page's player. */
export type ScoutReport = {
  id: string;
  title: string;
  match: ScoutMatch;
  /** Where the player was found in the file, or null when the file does not hold them. */
  player: { number: number; team: string } | null;
  /** The stats per skill; empty when the player was not found. */
  skills: ScoutSkillStats[];
};

/** One scout file in the player's list. */
export type PlayerScout = {
  id: string;
  title: string;
  /** The same-origin route that answers the report; read when the file is opened. */
  reportPath: string;
};

/**
 * Writes a percentage: whole percent, a real minus sign for negative values, and a dash when there is none.
 *
 * @param value - The percentage, or null.
 * @returns The text, e.g. `43%`, `−100%` or `—`.
 */
export function formatRate(value: number | null): string {
  if (value === null) {
    return "—";
  }
  return value < 0 ? `−${Math.abs(value)}%` : `${value}%`;
}

/** Month names for {@link formatMatchDate}; the host may have no locale data, so they are listed. */
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * Writes a match date the British way.
 *
 * @param iso - `YYYY-MM-DD`, or null.
 * @returns For example `12 Mar 2026`, or an empty string when the date is unknown.
 */
export function formatMatchDate(iso: string | null): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? "");
  if (!parts) {
    return "";
  }
  return `${Number(parts[3])} ${MONTHS[Number(parts[2]) - 1] ?? ""} ${parts[1]}`;
}

/**
 * Lists every set's score, home first.
 *
 * @param match - The match header.
 * @returns For example `25–21 · 22–25`.
 */
export function setScores(match: ScoutMatch): string {
  return match.sets.map((set) => `${set.home}–${set.away}`).join(" · ");
}

/**
 * Tells the result from the player's team's side.
 *
 * @param report - The report.
 * @returns `Won 3–1` or `Lost 1–3`, or the plain home–away score when the player is not in the file.
 */
export function matchResult(report: ScoutReport): string {
  const { homeSets, awaySets, homeTeam } = report.match;
  if (!report.player) {
    return `${homeSets}–${awaySets}`;
  }
  const isHome = report.player.team === homeTeam;
  const own = isHome ? homeSets : awaySets;
  const other = isHome ? awaySets : homeSets;
  return `${own > other ? "Won" : "Lost"} ${own}–${other}`;
}
