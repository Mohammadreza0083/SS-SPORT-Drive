/** UI copy of the Roster page (English; kept out of the components). */
export const rosterCopy = {
  pageTitle: "Roster",
  eyebrow: (year: number) => `SS-SPORT PLAYERS · ${year}`,
  sharedWith: "Shared privately with",
  playerCount: (count: number) => `${count} ${count === 1 ? "player" : "players"}`,
  filterLabel: "Filter players by position",
  allPositions: "All",
  positions: ["S", "OH", "OPP", "MB", "L", "DS"] as const,
  videoCount: (count: number) => `${count} ${count === 1 ? "video" : "videos"}`,
  viewPlayer: "View player →",
};
