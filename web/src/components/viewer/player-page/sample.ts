import type { PlayerPageData } from "./types";

/** A public multi-rendition HLS test stream (Mux), standing in for real videos until ingest exists. */
const TEST_STREAM = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8";

/**
 * The design canvas's sample player, used by the development preview and the tests.
 * Never shown in production.
 */
export const samplePlayerPage: PlayerPageData = {
  player: {
    name: "Arash Tehrani",
    firstName: "Arash",
    position: { code: "OPP", label: "Opposite" },
    nationality: { code: "IR", name: "Iran" },
    volleyboxUrl: "https://volleybox.net",
  },
  viewerEmail: "j.demir@aurora-volley.example",
  recipient: { organisation: "Club Aurora", name: "Jan Demir" },
  note: { text: "Hi Jan, as discussed: Arash's 2026 season. Full match vs Paykan included.", author: "SS-SPORT Scouting" },
  stats: { videos: 6, minutes: 117, documents: 2 },
  videos: [
    { id: "v1", title: "Highlights 2026", tag: "Highlight", durationSeconds: 84, src: TEST_STREAM, fps: 24 },
    { id: "v2", title: "Spikes vs Northbridge", tag: "Spike", durationSeconds: 192, src: TEST_STREAM, fps: 24 },
    { id: "v3", title: "Serve compilation", tag: "Serve", durationSeconds: 125, src: TEST_STREAM, fps: 24 },
    { id: "v4", title: "Block & defence", tag: "Block", durationSeconds: 160, src: TEST_STREAM, fps: 24 },
    { id: "v5", title: "Full match vs Paykan", tag: "Full match", durationSeconds: 6490, src: TEST_STREAM, fps: 24 },
  ],
  documents: [
    { id: "d1", title: "Season stats 2026", pageCount: 2, pages: ["/samples/season-stats-1.svg", "/samples/season-stats-2.svg"] },
    { id: "d2", title: "Player CV", pageCount: 1, pages: ["/samples/player-cv-1.svg"] },
  ],
};
