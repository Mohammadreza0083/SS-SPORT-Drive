import { describe, expect, it, vi } from "vitest";
import { MAX_HEARTBEAT_SECONDS, WatchTracker, type WatchTransport } from "./watch-tracker";
import { TrackingRequestError } from "./tracking-client";

/**
 * The player's side of watch tracking (backlog W-2, the client half of L-5). The tracker is pure: the player hook feeds
 * it the media element's events and calls `tick()` every `heartbeatSeconds`; it decides what to send.
 *
 * - The first play starts the watch (`POST …/watch`, one play on the server) and sends no `Play` event.
 * - Every tick sends the range played since the last heartbeat, measured on the video's own clock (`timeupdate`), so
 *   2× speed and stalls are counted right. A range longer than 30 s (the API's cap) goes as several heartbeats.
 * - Pause, seek and end first close the open range with a heartbeat, then send their event. A seek closes the range at
 *   the last position seen before the jump and opens a new one at the new position.
 * - Playing again after a pause or the end sends `Play` (after `Ended`, the server counts a new play).
 * - Everything is sent in order, even while the start is still on its way.
 * - Tracking never breaks playback: a failed start turns it off, a 401 or 404 (session gone, watch unknown) turns it
 *   off, any other failure is dropped and tracking goes on.
 */

const WATCH = "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c";

/** What a stub transport recorded, in order. */
type Call = ["start"] | ["heartbeat", string, number, number] | ["event", string, string, number];

/**
 * A transport stub that records every call in one list, so the order across kinds can be checked.
 *
 * @param overrides - Replacements for single methods.
 * @returns The transport and its call list.
 */
function transport(overrides: Partial<WatchTransport> = {}) {
  const calls: Call[] = [];
  const stub: WatchTransport = {
    start: vi.fn(async () => {
      calls.push(["start"]);
      return { watchId: WATCH, heartbeatSeconds: 10 };
    }),
    heartbeat: vi.fn(async (watchId: string, from: number, to: number) => {
      calls.push(["heartbeat", watchId, from, to]);
    }),
    event: vi.fn(async (watchId: string, type: string, position: number) => {
      calls.push(["event", watchId, type, position]);
    }),
    ...overrides,
  };
  return { stub, calls };
}

/**
 * Plays from `from` to `to` on the video's clock, one `timeupdate` per second, as a browser reports it.
 *
 * @param tracker - The tracker.
 * @param from - The first position.
 * @param to - The last position.
 */
function playClock(tracker: WatchTracker, from: number, to: number) {
  for (let at = from; at <= to; at++) {
    tracker.timeUpdate(at);
  }
}

describe("WatchTracker", () => {
  it("starts the watch on the first play, once, with no Play event", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);

    await tracker.play(0);

    expect(calls).toEqual([["start"]]);
    expect(tracker.heartbeatSeconds).toBe(10);
  });

  it("uses the heartbeat interval the API answers", async () => {
    const { stub } = transport({ start: vi.fn(async () => ({ watchId: WATCH, heartbeatSeconds: 15 })) });
    const tracker = new WatchTracker(stub);

    await tracker.play(0);

    expect(tracker.heartbeatSeconds).toBe(15);
  });

  it("sends on each tick the range played since the last heartbeat", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);

    playClock(tracker, 0, 10);
    await tracker.tick();
    playClock(tracker, 11, 20);
    await tracker.tick();

    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 0, 10],
      ["heartbeat", WATCH, 10, 20],
    ]);
  });

  it("sends nothing on a tick when nothing new was played, or while paused", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 5);
    await tracker.tick();

    await tracker.tick();
    await tracker.pause(5);
    await tracker.tick();

    expect(calls.filter((call) => call[0] === "heartbeat")).toEqual([["heartbeat", WATCH, 0, 5]]);
  });

  it("counts on the video's clock, splitting a long range into heartbeats of at most 30 s", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);

    // A throttled background tab at 2× speed: 75 s of video between two ticks.
    playClock(tracker, 0, 75);
    await tracker.tick();

    expect(MAX_HEARTBEAT_SECONDS).toBe(30);
    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 0, 30],
      ["heartbeat", WATCH, 30, 60],
      ["heartbeat", WATCH, 60, 75],
    ]);
  });

  it("closes the open range before a pause, then sends Pause", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 7);

    await tracker.pause(7);

    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 0, 7],
      ["event", WATCH, "Pause", 7],
    ]);
  });

  it("closes the range at the last position before a seek, sends Seek and opens a new range there", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 12);

    await tracker.seeked(40);
    playClock(tracker, 40, 50);
    await tracker.tick();

    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 0, 12],
      ["event", WATCH, "Seek", 40],
      ["heartbeat", WATCH, 40, 50],
    ]);
  });

  it("sends Seek with no heartbeat for a seek while paused, and plays on from the new position", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 4);
    await tracker.pause(4);

    await tracker.seeked(90);
    await tracker.play(90);
    playClock(tracker, 90, 95);
    await tracker.tick();

    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 0, 4],
      ["event", WATCH, "Pause", 4],
      ["event", WATCH, "Seek", 90],
      ["event", WATCH, "Play", 90],
      ["heartbeat", WATCH, 90, 95],
    ]);
  });

  it("closes the range at the end, sends Ended, and sends Play when the viewer plays it again", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(80);
    playClock(tracker, 80, 84);

    await tracker.ended(84);
    await tracker.seeked(0);
    await tracker.play(0);

    expect(stub.start).toHaveBeenCalledTimes(1);
    expect(calls.slice(1)).toEqual([
      ["heartbeat", WATCH, 80, 84],
      ["event", WATCH, "Ended", 84],
      ["event", WATCH, "Seek", 0],
      ["event", WATCH, "Play", 0],
    ]);
  });

  it("resumes after a pause with a Play event and no new start", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    await tracker.pause(0);

    await tracker.play(0);

    expect(stub.start).toHaveBeenCalledTimes(1);
    expect(calls.at(-1)).toEqual(["event", WATCH, "Play", 0]);
  });

  it("keeps the order while the start is still on its way", async () => {
    let answer: (value: { watchId: string; heartbeatSeconds: number }) => void = () => {};
    const { stub, calls } = transport({
      start: vi.fn(
        () =>
          new Promise<{ watchId: string; heartbeatSeconds: number }>((resolve) => {
            calls.push(["start"]);
            answer = resolve;
          }),
      ),
    });
    const tracker = new WatchTracker(stub);

    const started = tracker.play(0);
    playClock(tracker, 0, 3);
    const paused = tracker.pause(3);
    answer({ watchId: WATCH, heartbeatSeconds: 10 });
    await Promise.all([started, paused]);

    expect(calls).toEqual([
      ["start"],
      ["heartbeat", WATCH, 0, 3],
      ["event", WATCH, "Pause", 3],
    ]);
  });

  it("turns itself off when the start fails, without throwing", async () => {
    const { stub, calls } = transport({ start: vi.fn(async () => Promise.reject(new TrackingRequestError(401))) });
    const tracker = new WatchTracker(stub);

    await expect(tracker.play(0)).resolves.toBeUndefined();
    playClock(tracker, 0, 10);
    await tracker.tick();
    await tracker.pause(10);

    expect(calls).toEqual([]);
    expect(tracker.stopped).toBe(true);
  });

  it.each([401, 404])("stops sending after a %i (session gone or watch unknown)", async (status) => {
    const { stub, calls } = transport({
      heartbeat: vi.fn(async () => Promise.reject(new TrackingRequestError(status))),
    });
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 10);

    await expect(tracker.tick()).resolves.toBeUndefined();
    playClock(tracker, 11, 20);
    await tracker.tick();
    await tracker.pause(20);

    expect(stub.heartbeat).toHaveBeenCalledTimes(1);
    expect(calls).toEqual([["start"]]);
    expect(tracker.stopped).toBe(true);
  });

  it("drops any other failure and goes on", async () => {
    const heartbeat = vi.fn<WatchTransport["heartbeat"]>().mockRejectedValueOnce(new TrackingRequestError(503)).mockResolvedValue(undefined);
    const { stub } = transport({ heartbeat });
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 10);

    await expect(tracker.tick()).resolves.toBeUndefined();
    playClock(tracker, 11, 20);
    await tracker.tick();

    expect(heartbeat).toHaveBeenNthCalledWith(2, WATCH, 10, 20);
    expect(tracker.stopped).toBe(false);
  });

  it("closes the open range when the video is left, then sends nothing more", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);
    await tracker.play(0);
    playClock(tracker, 0, 6);

    await tracker.dispose();
    playClock(tracker, 7, 20);
    await tracker.tick();

    expect(calls.slice(1)).toEqual([["heartbeat", WATCH, 0, 6]]);
    expect(tracker.stopped).toBe(true);
  });

  it("sends nothing for a video that was never played", async () => {
    const { stub, calls } = transport();
    const tracker = new WatchTracker(stub);

    tracker.timeUpdate(3);
    await tracker.tick();
    await tracker.seeked(20);
    await tracker.dispose();

    expect(calls).toEqual([]);
  });
});
