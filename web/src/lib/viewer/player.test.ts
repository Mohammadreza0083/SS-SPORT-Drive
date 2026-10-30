import { describe, expect, it } from "vitest";
import { HLS_CONFIG, PLAYBACK_RATES, frameDirection, frameStep, playbackMode } from "./player";

describe("PLAYBACK_RATES", () => {
  it("offers 0.25× to 2× (brief §10)", () => {
    expect(PLAYBACK_RATES).toEqual([0.25, 0.5, 0.75, 1, 1.25, 1.5, 2]);
  });
});

describe("HLS_CONFIG", () => {
  it("starts at the lowest rendition (360p) for a fast first frame", () => {
    expect(HLS_CONFIG.startLevel).toBe(0);
  });
});

describe("frameStep", () => {
  it("moves one frame forward at the given frame rate", () => {
    expect(frameStep(10, 60, 1, 25)).toBeCloseTo(10.04, 5);
  });

  it("moves one frame back", () => {
    expect(frameStep(10, 60, -1, 30)).toBeCloseTo(10 - 1 / 30, 5);
  });

  it("never goes before the start", () => {
    expect(frameStep(0.01, 60, -1, 30)).toBe(0);
  });

  it("never goes past the end", () => {
    expect(frameStep(59.99, 60, 1, 30)).toBe(60);
  });

  it("stays put while the duration is unknown", () => {
    expect(frameStep(0, Number.NaN, 1, 30)).toBe(0);
  });
});

describe("frameDirection", () => {
  it.each([
    [",", -1],
    [".", 1],
  ] as const)("maps %s to %i", (key, direction) => {
    expect(frameDirection(key)).toBe(direction);
  });

  it("ignores other keys", () => {
    expect(frameDirection("k")).toBeNull();
  });
});

describe("playbackMode", () => {
  it("plays natively on iPhone and iPad Safari", () => {
    expect(playbackMode({ canPlayNativeHls: true, hasMediaSource: true, isAppleMobile: true })).toBe("native");
  });

  it("plays natively where Media Source Extensions are missing", () => {
    expect(playbackMode({ canPlayNativeHls: true, hasMediaSource: false, isAppleMobile: false })).toBe("native");
  });

  it("uses hls.js on desktops, so the quality menu and the 360p start work", () => {
    expect(playbackMode({ canPlayNativeHls: true, hasMediaSource: true, isAppleMobile: false })).toBe("hls");
    expect(playbackMode({ canPlayNativeHls: false, hasMediaSource: true, isAppleMobile: false })).toBe("hls");
  });

  it("reports unsupported when neither works", () => {
    expect(playbackMode({ canPlayNativeHls: false, hasMediaSource: false, isAppleMobile: false })).toBe("unsupported");
  });
});
