import type { HomeData, LockedData, PublicPlayer } from "./types";

/**
 * Builds a sample player with a slug taken from the name.
 *
 * @param name - Full name.
 * @param position - Position code.
 * @param nationality - Nationality.
 * @param videos - Video count.
 * @param documents - Document count.
 * @returns The player.
 */
function player(name: string, position: string, nationality: string, videos: number, documents: number): PublicPlayer {
  return { slug: name.toLowerCase().replace(/\s+/g, "-"), name, position, nationality, videos, documents };
}

/** The canvas's `Home` sample: eight players, nobody signed in. Used by `/design/home` until the search API exists. */
export const sampleHome: HomeData = {
  players: [
    player("Arash Tehrani", "OPP", "Iran", 6, 2),
    player("Reza Moradi", "S", "Iran", 5, 1),
    player("Kian Farhadi", "OH", "Iran", 4, 1),
    player("Milad Sadeghi", "MB", "Iran", 3, 1),
    player("Pouya Rahimi", "L", "Iran", 6, 2),
    player("Omid Kazemi", "OH", "Iran", 9, 0),
    player("Daniyar Seitov", "OH", "Kazakhstan", 4, 1),
    player("Hossein Karimi", "DS", "Iran", 4, 1),
  ],
  viewer: null,
};

/** The canvas's `Locked` sample: Arash Tehrani with five locked videos and two locked documents. */
export const sampleLocked: LockedData = {
  player: { ...player("Arash Tehrani", "OPP", "Iran", 6, 2), minutes: 117, volleyboxUrl: "https://volleybox.net/arash-tehrani-p00000" },
  videos: [
    { title: "Highlights 2026", tag: "Highlight", duration: "1:24" },
    { title: "Spikes vs Northbridge", tag: "Spike", duration: "3:12" },
    { title: "Serve compilation", tag: "Serve", duration: "2:05" },
    { title: "Block & defence", tag: "Block", duration: "2:40" },
    { title: "Full match vs Paykan", tag: "Full match", duration: "1:48:10" },
  ],
  documents: [
    { title: "Season stats 2026", pages: 2 },
    { title: "Player CV", pages: 1 },
  ],
  viewer: null,
};
