import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";

/** The props the stand-in player was last rendered with. */
const rendered: Array<Record<string, unknown>> = [];

vi.mock("@/components/viewer/player/video-player", () => ({
  /**
   * Stands in for the real player and records its props.
   *
   * @param props - The props the section passes.
   * @returns A plain marker.
   */
  VideoPlayer: (props: Record<string, unknown>) => {
    rendered.push(props);
    return <div data-testid="player">{String(props.title)}</div>;
  },
}));

const { VideosSection } = await import("./videos-section");

/**
 * The player page hands the current video's watch route to the player (W-2), so watching is reported for the video
 * on screen; picking another video hands over that video's route.
 */

afterEach(() => {
  cleanup();
  rendered.length = 0;
  vi.unstubAllGlobals();
});

describe("VideosSection watch tracking", () => {
  it("passes the current video's watch path to the player, and the next one's when another is picked", () => {
    vi.stubGlobal("fetch", vi.fn<typeof fetch>(async () => Response.json({ playlistUrl: "/hls/t/x/master.m3u8" })));
    render(
      <VideosSection
        videos={[
          { id: "v1", title: "Highlights 2026", tag: "Highlight", durationSeconds: 84, src: "", streamPath: "/api/links/Ab3xYz/players/p/videos/v1/stream", watchPath: "/api/links/Ab3xYz/players/p/videos/v1/watch" },
          { id: "v2", title: "Full match", tag: "Full match", durationSeconds: 6490, src: "", streamPath: "/api/links/Ab3xYz/players/p/videos/v2/stream", watchPath: "/api/links/Ab3xYz/players/p/videos/v2/watch" },
        ]}
      />,
    );
    expect(rendered.at(-1)?.watchPath).toBe("/api/links/Ab3xYz/players/p/videos/v1/watch");

    // The tag chip "Full match" comes first; the playlist item is the one inside the list.
    fireEvent.click(within(screen.getByRole("list")).getByRole("button", { name: /Full match/ }));

    expect(rendered.at(-1)?.watchPath).toBe("/api/links/Ab3xYz/players/p/videos/v2/watch");
  });

  it("passes no watch path for sample videos (the design preview)", () => {
    render(<VideosSection videos={[{ id: "v1", title: "Sample", tag: "Highlight", durationSeconds: 84, src: "https://example.test/v/master.m3u8" }]} />);

    expect(rendered.at(-1)?.watchPath).toBeUndefined();
  });
});
