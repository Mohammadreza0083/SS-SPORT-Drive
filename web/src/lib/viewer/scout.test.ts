import { describe, expect, it } from "vitest";
import { formatMatchDate, formatRate, matchResult, setScores, type ScoutReport } from "./scout";

/** How a scout report (V-8) is put into words on the page. */

/** Arash's match: Club Aurora (his team, home) beat Volley Nord 3-1. */
const report: ScoutReport = {
  id: "s1",
  title: "Match vs Volley Nord",
  match: {
    date: "2026-03-12",
    homeTeam: "Club Aurora",
    awayTeam: "Volley Nord",
    homeSets: 3,
    awaySets: 1,
    sets: [
      { home: 25, away: 21 },
      { home: 22, away: 25 },
      { home: 25, away: 19 },
      { home: 26, away: 24 },
    ],
  },
  player: { number: 7, team: "Club Aurora" },
  skills: [],
};

describe("formatRate", () => {
  it("shows whole percent, a minus for negative values, and a dash when there is no rate", () => {
    expect(formatRate(43)).toBe("43%");
    expect(formatRate(0)).toBe("0%");
    expect(formatRate(-100)).toBe("−100%");
    expect(formatRate(null)).toBe("—");
  });
});

describe("formatMatchDate", () => {
  it("writes the date the British way, and nothing when it is unknown", () => {
    expect(formatMatchDate("2026-03-12")).toBe("12 Mar 2026");
    expect(formatMatchDate(null)).toBe("");
  });
});

describe("setScores", () => {
  it("lists every set's score, home first", () => {
    expect(setScores(report.match)).toBe("25–21 · 22–25 · 25–19 · 26–24");
  });
});

describe("matchResult", () => {
  it("tells the result from the player's team's side", () => {
    expect(matchResult(report)).toBe("Won 3–1");
    expect(matchResult({ ...report, player: { number: 7, team: "Volley Nord" } })).toBe("Lost 1–3");
  });

  it("gives the plain score when the player is not in the file", () => {
    expect(matchResult({ ...report, player: null })).toBe("3–1");
  });
});
