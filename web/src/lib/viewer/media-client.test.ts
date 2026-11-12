import { describe, expect, it, vi } from "vitest";
import { fetchPages, fetchScoutReport, fetchStreamUrl, pagesPath, scoutPath, scoutsPath, streamPath } from "./media-client";

/**
 * The browser side of signed media (V-5b) and scout reports (V-8): the same-origin paths, and small fetch helpers the
 * player, the document viewer and the scout view call. They send the session cookie (same origin), never cache, and
 * throw on any answer that is not 2xx so the caller can retry or show its error.
 */

const PLAYER = "0b6f2c1e-1111-4a4a-8888-000000000001";
const ITEM = "0b6f2c1e-2222-4a4a-8888-000000000002";

describe("paths", () => {
  it("builds the same-origin routes, with the code encoded", () => {
    expect(streamPath("Ab3xYz", PLAYER, ITEM)).toBe(`/api/links/Ab3xYz/players/${PLAYER}/videos/${ITEM}/stream`);
    expect(pagesPath("Ab3xYz", PLAYER, ITEM)).toBe(`/api/links/Ab3xYz/players/${PLAYER}/documents/${ITEM}/pages`);
    expect(scoutPath("Ab3xYz", PLAYER, ITEM)).toBe(`/api/links/Ab3xYz/players/${PLAYER}/scouts/${ITEM}`);
    expect(scoutsPath("Ab3xYz", PLAYER)).toBe(`/api/links/Ab3xYz/players/${PLAYER}/scouts`);
    expect(streamPath("A b", PLAYER, ITEM)).toContain("/api/links/A%20b/");
  });
});

/**
 * A fetch stub that answers once.
 *
 * @param status - The status.
 * @param body - The JSON body.
 * @returns The stub.
 */
function answer(status: number, body: unknown = {}) {
  return vi.fn<typeof fetch>(async () => Response.json(body, { status }));
}

describe("fetchStreamUrl", () => {
  it("returns the signed playlist URL, asking same-origin and uncached", async () => {
    const fetchMock = answer(200, { playlistUrl: "/hls/tok/players/p/videos/v/v1/hls/master.m3u8", expiresAt: "2026-11-12T10:30:00Z" });

    const url = await fetchStreamUrl("/api/links/Ab3xYz/players/p/videos/v/stream", fetchMock);

    expect(url).toBe("/hls/tok/players/p/videos/v/v1/hls/master.m3u8");
    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(init.credentials).toBe("same-origin");
    expect(init.cache).toBe("no-store");
  });

  it("throws on an answer that is not 2xx", async () => {
    await expect(fetchStreamUrl("/x", answer(401))).rejects.toThrow();
  });

  it("refuses a playlist URL that is not on the edge's /hls/ path", async () => {
    await expect(fetchStreamUrl("/x", answer(200, { playlistUrl: "https://evil.example/a.m3u8" }))).rejects.toThrow();
  });
});

describe("fetchPages", () => {
  it("returns the signed page URLs in order", async () => {
    const fetchMock = answer(200, { pages: ["/pages/t/f/1.webp", "/pages/t/f/2.webp"], expiresAt: "2026-11-12T10:30:00Z" });

    expect(await fetchPages("/api/x/pages", fetchMock)).toEqual(["/pages/t/f/1.webp", "/pages/t/f/2.webp"]);
  });

  it("drops anything that is not an edge page URL", async () => {
    const fetchMock = answer(200, { pages: ["/pages/t/f/1.webp", "javascript:alert(1)", "https://evil.example/2.webp"] });

    expect(await fetchPages("/api/x/pages", fetchMock)).toEqual(["/pages/t/f/1.webp"]);
  });

  it("throws on an answer that is not 2xx", async () => {
    await expect(fetchPages("/x", answer(404))).rejects.toThrow();
  });
});

describe("fetchScoutReport", () => {
  it("returns the report as the API answers it", async () => {
    const report = {
      id: ITEM,
      title: "Match vs Volley Nord",
      match: { date: "2026-03-12", homeTeam: "Club Aurora", awayTeam: "Volley Nord", homeSets: 3, awaySets: 1, sets: [{ home: 25, away: 21 }] },
      player: { number: 7, team: "Club Aurora" },
      skills: [],
    };

    expect(await fetchScoutReport("/api/x/scouts/s", answer(200, report))).toEqual(report);
  });

  it("throws on an answer that is not 2xx, with the status", async () => {
    await expect(fetchScoutReport("/x", answer(422))).rejects.toMatchObject({ status: 422 });
  });
});
