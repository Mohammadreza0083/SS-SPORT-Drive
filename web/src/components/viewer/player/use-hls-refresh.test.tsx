import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { REFRESH_EVERY_MS } from "@/lib/viewer/hls-token";
import { useHlsRefresh } from "./use-hls-refresh";

/** A stand-in media element whose playing state the test controls. */
type FakeMedia = HTMLVideoElement & { paused: boolean };

/**
 * Builds a media element that reports itself as playing or paused.
 *
 * @param paused - Whether it starts paused.
 * @returns The element.
 */
function media(paused: boolean): FakeMedia {
  const element = document.createElement("video") as FakeMedia;
  Object.defineProperty(element, "paused", { value: paused, writable: true, configurable: true });
  element.play = vi.fn().mockResolvedValue(undefined);
  return element;
}

/** Lets pending promise callbacks run. */
const flush = () => act(async () => await Promise.resolve());

const FIRST = "/hls/tok1.sig/players/a/videos/b/hls/master.m3u8";
const SECOND = "/hls/tok2.sig/players/a/videos/b/hls/master.m3u8";

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("useHlsRefresh", () => {
  it("knows the current token from the source", () => {
    const element = media(false);
    const { result } = renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "hls", src: FIRST }));

    expect(result.current.current).toBe("tok1.sig");
  });

  it("renews the token after 20 minutes of watching, without touching the stream (hls.js)", async () => {
    const element = media(false);
    const refreshSource = vi.fn().mockResolvedValue(SECOND);
    const { result } = renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "hls", src: FIRST, refreshSource }));

    act(() => vi.advanceTimersByTime(REFRESH_EVERY_MS - 60_000));
    expect(refreshSource).not.toHaveBeenCalled();

    act(() => vi.advanceTimersByTime(60_000));
    await flush();

    expect(refreshSource).toHaveBeenCalledTimes(1);
    expect(result.current.current).toBe("tok2.sig");
    expect(element.getAttribute("src")).toBeNull();
  });

  it("does not renew while the video is paused, but does on play once the token is old", async () => {
    const element = media(true);
    const refreshSource = vi.fn().mockResolvedValue(SECOND);
    renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "hls", src: FIRST, refreshSource }));

    act(() => vi.advanceTimersByTime(REFRESH_EVERY_MS * 2));
    expect(refreshSource).not.toHaveBeenCalled();

    element.paused = false;
    act(() => {
      element.dispatchEvent(new Event("play"));
    });
    await flush();

    expect(refreshSource).toHaveBeenCalledTimes(1);
  });

  it("swaps the source on native playback, keeping the position and resuming", async () => {
    const element = media(false);
    element.currentTime = 754;
    const refreshSource = vi.fn().mockResolvedValue(SECOND);
    renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "native", src: FIRST, refreshSource }));

    act(() => vi.advanceTimersByTime(REFRESH_EVERY_MS));
    await flush();
    expect(element.getAttribute("src")).toBe(SECOND);

    act(() => {
      element.dispatchEvent(new Event("loadedmetadata"));
    });

    expect(element.currentTime).toBe(754);
    expect(element.play).toHaveBeenCalled();
  });

  it("tries again at the next check when a renewal fails", async () => {
    const element = media(false);
    const refreshSource = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue(SECOND);
    const { result } = renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "hls", src: FIRST, refreshSource }));

    act(() => vi.advanceTimersByTime(REFRESH_EVERY_MS));
    await flush();
    act(() => vi.advanceTimersByTime(60_000));
    await flush();

    expect(refreshSource).toHaveBeenCalledTimes(2);
    expect(result.current.current).toBe("tok2.sig");
  });

  it("does nothing without a way to renew", () => {
    const element = media(false);
    const { result } = renderHook(() => useHlsRefresh({ mediaRef: { current: element }, mode: "hls", src: FIRST }));

    act(() => vi.advanceTimersByTime(REFRESH_EVERY_MS * 3));

    expect(result.current.current).toBe("tok1.sig");
  });
});
