import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createElement, forwardRef, type ReactNode } from "react";

/**
 * Stands in for one Media Chrome React component: renders its custom element with the given props,
 * so the tests can check what the player passes without the real web components.
 */
function stub(tag: string) {
  const Component = forwardRef<HTMLElement, Record<string, unknown> & { children?: ReactNode }>(
    function Stub({ children, ...props }, ref) {
      const attrs = Object.fromEntries(
        Object.entries(props).map(([k, v]) => [
          k === "className" ? "class" : k.toLowerCase(),
          Array.isArray(v) ? v.join(" ") : typeof v === "boolean" ? String(v) : v,
        ]),
      );
      return createElement(tag, { ...attrs, ref } as never, children as ReactNode);
    },
  );
  return Component;
}

vi.mock("media-chrome/react", () => ({
  MediaController: stub("media-controller"),
  MediaControlBar: stub("media-control-bar"),
  MediaPlayButton: stub("media-play-button"),
  MediaTimeRange: stub("media-time-range"),
  MediaTimeDisplay: stub("media-time-display"),
  MediaMuteButton: stub("media-mute-button"),
  MediaVolumeRange: stub("media-volume-range"),
  MediaFullscreenButton: stub("media-fullscreen-button"),
  MediaLoadingIndicator: stub("media-loading-indicator"),
}));
vi.mock("media-chrome/react/menu", () => ({
  MediaPlaybackRateMenu: stub("media-playback-rate-menu"),
  MediaPlaybackRateMenuButton: stub("media-playback-rate-menu-button"),
  MediaRenditionMenu: stub("media-rendition-menu"),
  MediaRenditionMenuButton: stub("media-rendition-menu-button"),
}));
vi.mock("hls-video-element", () => ({}));

const { VideoPlayer } = await import("./video-player");

/** Makes the page look like a desktop browser with Media Source Extensions (hls.js mode). */
function desktop() {
  vi.stubGlobal("MediaSource", class {});
}

beforeEach(() => {
  vi.spyOn(HTMLMediaElement.prototype, "pause").mockImplementation(() => {});
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/**
 * Waits for the player to choose its playback mode and mount the media element.
 *
 * @returns The media element.
 */
async function mediaElement(container: HTMLElement): Promise<HTMLVideoElement> {
  // The first dynamic import of hls-video-element can take longer than the 1 s default.
  await waitFor(() => expect(container.querySelector("[slot=media]")).not.toBeNull(), { timeout: 5000 });
  return container.querySelector("[slot=media]") as HTMLVideoElement;
}

describe("VideoPlayer", () => {
  it("plays through hls.js on desktops, starting at 360p", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);

    const media = await mediaElement(container);
    expect(media.tagName).toBe("HLS-VIDEO");
    expect((media as unknown as { config: { startLevel: number } }).config.startLevel).toBe(0);
    expect(media.getAttribute("src")).toBe("https://example.test/v/master.m3u8");
  });

  it("plays natively where Media Source Extensions are missing", async () => {
    vi.spyOn(HTMLMediaElement.prototype, "canPlayType").mockReturnValue("maybe");
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);

    const media = await mediaElement(container);
    expect(media.tagName).toBe("VIDEO");
  });

  it("blocks the context menu over the player", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);
    await mediaElement(container);

    const event = new MouseEvent("contextmenu", { bubbles: true, cancelable: true });
    act(() => {
      container.querySelector("media-controller")!.dispatchEvent(event);
    });
    expect(event.defaultPrevented).toBe(true);
  });

  it("offers no download, picture-in-picture or casting", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);
    const media = await mediaElement(container);

    expect(media.hasAttribute("disablepictureinpicture")).toBe(true);
    expect(media.hasAttribute("disableremoteplayback")).toBe(true);
    expect(media.getAttribute("controlslist")).toContain("nodownload");
    expect(container.querySelector("a[download], [download]")).toBeNull();
    expect(container.textContent?.toLowerCase()).not.toContain("download ");
  });

  it("plays inline on phones", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);

    expect((await mediaElement(container)).hasAttribute("playsinline")).toBe(true);
  });

  it("steps one frame with the buttons, pausing first", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" fps={25} />);
    const media = await mediaElement(container);
    Object.defineProperty(media, "duration", { value: 60, configurable: true });
    const pause = vi.fn();
    Object.assign(media, { pause, currentTime: 10 });

    fireEvent.click(screen.getByRole("button", { name: "Next frame" }));
    expect(media.currentTime).toBeCloseTo(10.04, 5);
    fireEvent.click(screen.getByRole("button", { name: "Previous frame" }));
    fireEvent.click(screen.getByRole("button", { name: "Previous frame" }));
    expect(media.currentTime).toBeCloseTo(9.96, 5);
    expect(pause).toHaveBeenCalledTimes(3);
  });

  it("steps one frame with the , and . keys", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" fps={25} />);
    const media = await mediaElement(container);
    Object.defineProperty(media, "duration", { value: 60, configurable: true });
    media.currentTime = 10;
    const player = screen.getByRole("region", { name: "Video player: Highlights 2026" });

    fireEvent.keyDown(player, { key: "." });
    expect(media.currentTime).toBeCloseTo(10.04, 5);
    fireEvent.keyDown(player, { key: "," });
    expect(media.currentTime).toBeCloseTo(10, 5);
  });

  it("offers 0.25× to 2× and the quality menu", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);
    await mediaElement(container);

    expect(container.querySelector("media-playback-rate-menu")?.getAttribute("rates")).toBe("0.25 0.5 0.75 1 1.25 1.5 2");
    expect(container.querySelector("media-rendition-menu")).not.toBeNull();
  });

  it("starts playing on its own only when asked (after a pick from the playlist)", async () => {
    desktop();
    const { container, rerender } = render(<VideoPlayer src="https://example.test/v/a.m3u8" title="A" />);
    expect((await mediaElement(container)).hasAttribute("autoplay")).toBe(false);

    rerender(<VideoPlayer src="https://example.test/v/b.m3u8" title="B" autoPlay />);

    await waitFor(() => expect(container.querySelector("[slot=media]")?.hasAttribute("autoplay")).toBe(true));
  });

  it("sends every hls.js request with the latest edge token", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="/hls/tok1.sig/players/a/videos/b/hls/master.m3u8" title="Highlights 2026" />);
    const media = (await mediaElement(container)) as unknown as { config: { xhrSetup: (xhr: XMLHttpRequest, url: string) => void } };
    const xhr = { open: vi.fn() } as unknown as XMLHttpRequest;

    media.config.xhrSetup(xhr, "https://drive.ss-sport.co/hls/stale.sig/players/a/videos/b/hls/720p/seg_1.ts");

    expect(xhr.open).toHaveBeenCalledWith("GET", "https://drive.ss-sport.co/hls/tok1.sig/players/a/videos/b/hls/720p/seg_1.ts", true);
  });

  it("lays the viewer's email over the video when one is given", async () => {
    desktop();
    const { container } = render(
      <VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" viewerEmail="j.demir@aurora-volley.example" />,
    );
    await mediaElement(container);

    const overlay = container.querySelector(".ss-player-frame .ss-wm");
    expect(overlay?.textContent).toBe("j.demir@aurora-volley.example");
  });

  it("shows no overlay without a viewer email", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);
    await mediaElement(container);

    expect(container.querySelector(".ss-wm")).toBeNull();
  });

  it("takes the overlay into fullscreen by making the whole frame the fullscreen element", async () => {
    desktop();
    const { container } = render(
      <VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" viewerEmail="j.demir@aurora-volley.example" />,
    );
    await mediaElement(container);

    const controller = container.querySelector("media-controller") as HTMLElement & { fullscreenElement?: HTMLElement };
    expect(controller.fullscreenElement).toBe(container.querySelector(".ss-player-frame"));
  });

  it("states that the stream is protected", async () => {
    desktop();
    render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);

    expect(await screen.findByText("Protected stream · no downloads")).toBeTruthy();
  });

  it("carries the canvas's burned-in brand marks only in the video itself, not as page overlays", async () => {
    desktop();
    const { container } = render(<VideoPlayer src="https://example.test/v/master.m3u8" title="Highlights 2026" />);
    await mediaElement(container);

    expect(container.querySelector("img")).toBeNull();
  });
});
