/** Playback speeds the player offers (brief §10: 0.25× to 2×). */
export const PLAYBACK_RATES = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2] as const;

/**
 * hls.js settings. Playback starts at the lowest rendition (360p) so the first frame comes fast on a
 * mid-range phone over 4G (brief §10), then adaptive bitrate climbs as bandwidth allows.
 */
export const HLS_CONFIG = {
  startLevel: 0,
  capLevelToPlayerSize: true,
  backBufferLength: 30,
} as const;

/** Frame rate assumed when the video's own rate is not known yet. */
export const DEFAULT_FPS = 30;

/**
 * Works out the time one frame forward or back, kept inside the video.
 *
 * @param currentTime - The current position in seconds.
 * @param duration - The video length in seconds; NaN while unknown.
 * @param direction - 1 for the next frame, -1 for the previous one.
 * @param fps - The video's frame rate.
 * @returns The new position in seconds.
 */
export function frameStep(currentTime: number, duration: number, direction: 1 | -1, fps: number): number {
  if (!Number.isFinite(duration)) {
    return currentTime;
  }
  return Math.min(duration, Math.max(0, currentTime + direction / fps));
}

/**
 * Maps the frame-step keys: `,` steps back and `.` steps forward, as in common video tools.
 *
 * @param key - `KeyboardEvent.key`.
 * @returns The direction, or null for any other key.
 */
export function frameDirection(key: string): 1 | -1 | null {
  if (key === ",") {
    return -1;
  }
  if (key === ".") {
    return 1;
  }
  return null;
}

/** What the browser can do with HLS. */
export type HlsSupport = {
  /** `video.canPlayType('application/vnd.apple.mpegurl')` is not empty. */
  canPlayNativeHls: boolean;
  /** Media Source Extensions (or Apple's ManagedMediaSource) exist, so hls.js can run. */
  hasMediaSource: boolean;
  /** iPhone or iPad. */
  isAppleMobile: boolean;
};

/** How the player streams: through hls.js, through the browser's own HLS, or not at all. */
export type PlaybackMode = "hls" | "native" | "unsupported";

/**
 * Picks how to play HLS. iPhone and iPad Safari, and any browser without Media Source Extensions,
 * play natively, so hls.js is never downloaded there. Everywhere else hls.js runs, because only it
 * exposes the renditions for the quality menu and honours the 360p start.
 *
 * @param support - What the browser can do.
 * @returns The playback mode.
 */
export function playbackMode(support: HlsSupport): PlaybackMode {
  if (support.canPlayNativeHls && (support.isAppleMobile || !support.hasMediaSource)) {
    return "native";
  }
  if (support.hasMediaSource) {
    return "hls";
  }
  return "unsupported";
}

/**
 * Reads the current browser's HLS support.
 *
 * @returns What the browser can do.
 */
export function detectHlsSupport(): HlsSupport {
  const probe = document.createElement("video");
  const scope = globalThis as { MediaSource?: unknown; ManagedMediaSource?: unknown };
  const ua = navigator.userAgent;
  return {
    canPlayNativeHls: probe.canPlayType("application/vnd.apple.mpegurl") !== "",
    hasMediaSource: typeof scope.MediaSource !== "undefined" || typeof scope.ManagedMediaSource !== "undefined",
    isAppleMobile: /iPhone|iPad|iPod/.test(ua) || (ua.includes("Macintosh") && navigator.maxTouchPoints > 1),
  };
}
