import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, renderHook, waitFor } from "@testing-library/react";
import { useSignedSource } from "./use-signed-source";

/**
 * Wiring the player to the token endpoint (V-5b): a video on `/r/{code}` carries the same-origin path of its stream
 * route. The hook asks it for the signed playlist URL when the video is shown, and hands the player a `refreshSource`
 * that asks again, so the 30-minute edge token is renewed while the viewer watches (`useHlsRefresh`, V-5a).
 */

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

/**
 * A fetch stub that answers each call with the next playlist URL.
 *
 * @param urls - The URLs, in order.
 * @returns The stub.
 */
function signer(...urls: string[]) {
  let call = 0;
  return vi.fn<typeof fetch>(async () => Response.json({ playlistUrl: urls[Math.min(call++, urls.length - 1)], expiresAt: "2026-11-12T10:30:00Z" }));
}

describe("useSignedSource", () => {
  it("signs the stream when the video is shown", async () => {
    const fetchMock = signer("/hls/first/players/p/videos/v/v1/hls/master.m3u8");
    vi.stubGlobal("fetch", fetchMock);

    const { result } = renderHook(() => useSignedSource("/api/links/Ab3xYz/players/p/videos/v/stream", ""));

    expect(result.current.src).toBe("");
    await waitFor(() => expect(result.current.src).toBe("/hls/first/players/p/videos/v/v1/hls/master.m3u8"));
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("/api/links/Ab3xYz/players/p/videos/v/stream");
  });

  it("gives the player a refresh that signs again", async () => {
    vi.stubGlobal("fetch", signer("/hls/first/f/master.m3u8", "/hls/second/f/master.m3u8"));

    const { result } = renderHook(() => useSignedSource("/api/links/Ab3xYz/players/p/videos/v/stream", ""));
    await waitFor(() => expect(result.current.src).not.toBe(""));

    expect(await result.current.refreshSource!()).toBe("/hls/second/f/master.m3u8");
  });

  it("signs the new video when the viewer picks another one", async () => {
    const fetchMock = signer("/hls/a/f/master.m3u8", "/hls/b/g/master.m3u8");
    vi.stubGlobal("fetch", fetchMock);

    const { result, rerender } = renderHook(({ path }) => useSignedSource(path, ""), { initialProps: { path: "/api/x/videos/a/stream" } });
    await waitFor(() => expect(result.current.src).toBe("/hls/a/f/master.m3u8"));
    rerender({ path: "/api/x/videos/b/stream" });

    await waitFor(() => expect(result.current.src).toBe("/hls/b/g/master.m3u8"));
    expect(String(fetchMock.mock.calls[1]?.[0])).toBe("/api/x/videos/b/stream");
  });

  it("uses a fixed source as it is, with no refresh (the design preview's sample videos)", () => {
    const fetchMock = signer("/hls/unused/f/master.m3u8");
    vi.stubGlobal("fetch", fetchMock);

    const { result } = renderHook(() => useSignedSource(undefined, "/samples/highlights.m3u8"));

    expect(result.current.src).toBe("/samples/highlights.m3u8");
    expect(result.current.refreshSource).toBeUndefined();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("stays empty and reports the failure when signing is refused", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 503 })));

    const { result } = renderHook(() => useSignedSource("/api/x/videos/a/stream", ""));

    await waitFor(() => expect(result.current.failed).toBe(true));
    expect(result.current.src).toBe("");
  });
});
