import type { RosterData } from "./types";

/**
 * Builds a sample card.
 *
 * @param n - Its number, used for the id.
 * @param name - The full name.
 * @param position - The position code.
 * @param nationality - The country.
 * @param videos - The video count.
 * @returns The card.
 */
function card(n: number, name: string, position: string, nationality: string, videos: number) {
  const id = `sample-player-${n}`;
  return { id, href: `/design/player-page#${id}`, name, position, nationality, videos };
}

/**
 * The design canvas's sample roster (artboard `Roster`), used by the development preview and the tests.
 * Never shown in production.
 */
export const sampleRoster: RosterData = {
  sharedWith: "Club Aurora",
  playerCount: 9,
  players: [
    card(1, "Arash Tehrani", "OPP", "Iran", 6),
    card(2, "Reza Moradi", "S", "Iran", 5),
    card(3, "Kian Farhadi", "OH", "Iran", 4),
    card(4, "Milad Sadeghi", "MB", "Iran", 3),
    card(5, "Pouya Rahimi", "L", "Iran", 6),
    card(6, "Omid Kazemi", "OH", "Iran", 9),
    card(7, "Daniyar Seitov", "OH", "Kazakhstan", 4),
    card(8, "Sina Ahmadi", "S", "Iran", 2),
    card(9, "Hossein Karimi", "DS", "Iran", 4),
  ],
};
