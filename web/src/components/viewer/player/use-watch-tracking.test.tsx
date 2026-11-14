import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useWatchTracking } from "./use-watch-tracking";

/**
 * The player hook of watch tracking (backlog W-2): it listens to the media element and feeds a `WatchTracker`.
 *
 * - `play` → play; `timeupdate` → the video's clock; `pause` → pause (skipped when the video has ended: browsers fire
 *   `pause` just before `ended`); `seeking` → seek (the position is already the new one, and a `timeupdate` at the new
 *   position comes before `seeked`); `ended` → ended.
 * - While the video plays, a heartbeat goes every `heartbeatSeconds` (10 s).
 * - Leaving the video (another video picked, or the page closed) sends the last open range.
 * - No `watchPath` (the design preview) means no tracking at all.
 */

const WATCH = "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c";
const PATH = "/api/links/Ab3xYz/players/p/videos/v1/watch";
const OTHER_PATH = "/api/links/Ab3xYz/players/p/videos/v2/watch";

/** A stand-in media element whose clock the test moves. */
type FakeMedia = HTMLVideoElement & { currentTime: number; ended: boolean };

/**
 * Builds a media element with a writable clock and end flag.
 *
 * @returns The element.
 */
function media(): FakeMedia {
  const element = document.createElement("video") as FakeMedia;
  Object.defineProperty(element, "currentTime", { value: 0, writable: true, configurable: true });
  Object.defineProperty(element, "ended", { value: false, writable: true, configurable: true });
  return element;
}

/**
 * Moves the clock and fires the event the browser would.
 *
 * @param element - The media element.
 * @param type - The event.
 * @param at - The position to move to first, if any.
 */
async function fire(element: FakeMedia, type: string, at?: number) {
  await act(async () => {
    if (at !== undefined) {
      element.currentTime = at;
    }
    element.dispatchEvent(new Event(type));
    await Promise.resolve();
  });
}

/**
 * Plays on the video's clock one second at a time: each second the position moves on with a `timeupdate`, then the wall
 * clock runs one second (so a heartbeat due at 10 s sees the position of 10 s).
 *
 * @param element - The media element.
 * @param from - The position playing starts from.
 * @param to - The last position.
 */
async function playFor(element: FakeMedia, from: number, to: number) {
  for (let at = from + 1; at <= to; at++) {
    await fire(element, "timeupdate", at);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
  }
}

/**
 * What the stub fetch was asked, as `[path, body]` pairs.
 *
 * @param fetchMock - The stub.
 * @returns The calls.
 */
function sent(fetchMock: ReturnType<typeof vi.fn<typeof fetch>>): [string, unknown][] {
  return fetchMock.mock.calls.map(([url, init]) => [String(url), JSON.parse(String((init as RequestInit).body))]);
}

let fetchMock: ReturnType<typeof vi.fn<typeof fetch>>;

beforeEach(() => {
  vi.useFakeTimers();
  fetchMock = vi.fn<typeof fetch>(async (url) =>
    String(url).endsWith("/watch") ? Response.json({ watchId: WATCH, heartbeatSeconds: 10 }, { status: 201 }) : new Response(null, { status: 204 }),
  );
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("useWatchTracking", () => {
  it("starts the watch on the first play and sends a heartbeat every 10 seconds while playing", async () => {
    const element = media();
    renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: PATH }));

    await fire(element, "play", 0);
    await playFor(element, 0, 10);

    expect(sent(fetchMock)).toEqual([
      [PATH, {}],
      [`${PATH}/${WATCH}/heartbeat`, { from: 0, to: 10 }],
    ]);
  });

  it("sends the range and Pause on a pause, then no more heartbeats while paused", async () => {
    const element = media();
    renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: PATH }));
    await fire(element, "play", 0);
    await playFor(element, 0, 4);

    await fire(element, "pause", 4);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000);
    });

    expect(sent(fetchMock).slice(1)).toEqual([
      [`${PATH}/${WATCH}/heartbeat`, { from: 0, to: 4 }],
      [`${PATH}/${WATCH}/events`, { type: "Pause", position: 4 }],
    ]);
  });

  it("reports a seek from the seeking event, before the timeupdate at the new position", async () => {
    const element = media();
    renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: PATH }));
    await fire(element, "play", 0);
    await playFor(element, 0, 3);

    await fire(element, "seeking", 60);
    await fire(element, "timeupdate", 60);
    await fire(element, "seeked", 60);

    expect(sent(fetchMock).slice(1)).toEqual([
      [`${PATH}/${WATCH}/heartbeat`, { from: 0, to: 3 }],
      [`${PATH}/${WATCH}/events`, { type: "Seek", position: 60 }],
    ]);
  });

  it("sends Ended, not Pause, when the video plays to its end", async () => {
    const element = media();
    renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: PATH }));
    await fire(element, "play", 80);
    await playFor(element, 80, 84);

    element.ended = true;
    await fire(element, "pause", 84);
    await fire(element, "ended", 84);

    expect(sent(fetchMock).slice(1)).toEqual([
      [`${PATH}/${WATCH}/heartbeat`, { from: 80, to: 84 }],
      [`${PATH}/${WATCH}/events`, { type: "Ended", position: 84 }],
    ]);
  });

  it("sends the last open range when the player goes away", async () => {
    const element = media();
    const { unmount } = renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: PATH }));
    await fire(element, "play", 0);
    await playFor(element, 0, 6);

    unmount();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(sent(fetchMock).at(-1)).toEqual([`${PATH}/${WATCH}/heartbeat`, { from: 0, to: 6 }]);
    expect((fetchMock.mock.calls.at(-1)?.[1] as RequestInit).keepalive).toBe(true);
  });

  it("closes one video's watch and starts a new one when another video is picked", async () => {
    const element = media();
    const { rerender } = renderHook(({ path }) => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: path }), {
      initialProps: { path: PATH },
    });
    await fire(element, "play", 0);
    await playFor(element, 0, 5);

    rerender({ path: OTHER_PATH });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    await fire(element, "play", 0);

    expect(sent(fetchMock).slice(1)).toEqual([
      [`${PATH}/${WATCH}/heartbeat`, { from: 0, to: 5 }],
      [OTHER_PATH, {}],
    ]);
  });

  it("tracks nothing without a watch path (the design preview)", async () => {
    const element = media();
    renderHook(() => useWatchTracking({ mediaRef: { current: element }, ready: true, watchPath: undefined }));

    await fire(element, "play", 0);
    await playFor(element, 0, 12);
    await fire(element, "pause", 12);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("waits for the media element to be ready", async () => {
    const element = media();
    const { rerender } = renderHook(({ ready }) => useWatchTracking({ mediaRef: { current: element }, ready, watchPath: PATH }), {
      initialProps: { ready: false },
    });
    await fire(element, "play", 0);
    expect(fetchMock).not.toHaveBeenCalled();

    rerender({ ready: true });
    await fire(element, "play", 0);

    expect(sent(fetchMock)).toEqual([[PATH, {}]]);
  });
});
