import { describe, expect, it } from "vitest";
import { POSITION_FILTERS, filterPlayers, resultCountLabel } from "./search";
import type { PublicPlayer } from "./types";

/**
 * Builds a public player with sensible defaults.
 *
 * @param overrides - The fields that matter to a test.
 * @returns The player.
 */
function player(overrides: Partial<PublicPlayer>): PublicPlayer {
  return { slug: "p", name: "Arash Tehrani", position: "OPP", nationality: "Iran", videos: 6, documents: 2, ...overrides };
}

const players: PublicPlayer[] = [
  player({ slug: "arash-tehrani", name: "Arash Tehrani", position: "OPP" }),
  player({ slug: "reza-moradi", name: "Reza Moradi", position: "S" }),
  player({ slug: "kian-farhadi", name: "Kian Farhadi", position: "OH" }),
  player({ slug: "daniyar-seitov", name: "Daniyar Seitov", position: "OH", nationality: "Kazakhstan" }),
  player({ slug: "jose-nunez", name: "José Núñez", position: "L", nationality: "Spain" }),
];

describe("POSITION_FILTERS", () => {
  it("lists All and then the six positions in the canvas order", () => {
    expect(POSITION_FILTERS).toEqual(["All", "S", "OH", "OPP", "MB", "L", "DS"]);
  });
});

describe("filterPlayers", () => {
  it("keeps every player, in order, for an empty query and All", () => {
    expect(filterPlayers(players, "", "All")).toEqual(players);
  });

  it("matches any part of the name, ignoring case and surrounding spaces", () => {
    expect(filterPlayers(players, "  TEHR ", "All").map((p) => p.slug)).toEqual(["arash-tehrani"]);
    expect(filterPlayers(players, "an", "All").map((p) => p.slug)).toEqual(["arash-tehrani", "kian-farhadi", "daniyar-seitov"]);
  });

  it("ignores accents on both sides", () => {
    expect(filterPlayers(players, "nunez", "All").map((p) => p.slug)).toEqual(["jose-nunez"]);
    expect(filterPlayers(players, "JOSÉ", "All").map((p) => p.slug)).toEqual(["jose-nunez"]);
  });

  it("keeps only the chosen position", () => {
    expect(filterPlayers(players, "", "OH").map((p) => p.slug)).toEqual(["kian-farhadi", "daniyar-seitov"]);
  });

  it("combines the name and the position", () => {
    expect(filterPlayers(players, "kian", "OH").map((p) => p.slug)).toEqual(["kian-farhadi"]);
    expect(filterPlayers(players, "kian", "S")).toEqual([]);
  });

  it("does not change the list it was given", () => {
    const copy = [...players];
    filterPlayers(players, "reza", "S");
    expect(players).toEqual(copy);
  });
});

describe("resultCountLabel", () => {
  it("says player for one and players otherwise", () => {
    expect(resultCountLabel(0)).toBe("0 players");
    expect(resultCountLabel(1)).toBe("1 player");
    expect(resultCountLabel(8)).toBe("8 players");
  });
});
