"use client";

import { useEffect, useRef, type RefObject } from "react";
import { REFRESH_CHECK_MS, REFRESH_EVERY_MS, tokenFromUrl } from "@/lib/viewer/hls-token";
import type { PlaybackMode } from "@/lib/viewer/player";

/** What {@link useHlsRefresh} needs. */
type HlsRefreshOptions = {
  /** The media element being played. */
  mediaRef: RefObject<HTMLElement | null>;
  /** How the player streams. */
  mode: PlaybackMode | null;
  /** The current master playlist URL. */
  src: string;
  /** Fetches a freshly signed master playlist URL for the same video (wired to the API in V-5b). */
  refreshSource?: () => Promise<string>;
};

/** The media element's parts the hook uses. */
type RefreshableMedia = HTMLElement & { paused: boolean; currentTime: number; play?: () => Promise<void> };

/**
 * Keeps the edge HLS token fresh while the viewer watches (backlog V-5a). Every 20 minutes of playback, and on play
 * after a long pause, it asks for a newly signed URL:
 * - with hls.js, only the token changes; the player's request hook puts it into every following request, so the
 *   stream never stops;
 * - with native HLS (iPhone, iPad), the source is swapped and playback resumes at the same second.
 * Without `refreshSource` it only tracks the current token.
 *
 * @param options - See {@link HlsRefreshOptions}.
 * @returns A ref holding the latest token, read by the hls.js request hook.
 */
export function useHlsRefresh({ mediaRef, mode, src, refreshSource }: HlsRefreshOptions): RefObject<string | null> {
  const tokenRef = useRef<string | null>(tokenFromUrl(src));

  useEffect(() => {
    tokenRef.current = tokenFromUrl(src);
    const media = mediaRef.current as RefreshableMedia | null;
    if (!refreshSource || !media || !mode || mode === "unsupported") {
      return;
    }

    let lastRenewal = Date.now();
    let renewing = false;
    let active = true;

    /** Asks for a fresh URL and puts it to use; a failure is retried at the next check. */
    const renew = async () => {
      if (renewing) {
        return;
      }
      renewing = true;
      try {
        const next = await refreshSource();
        if (!active) {
          return;
        }
        lastRenewal = Date.now();
        tokenRef.current = tokenFromUrl(next);
        if (mode === "native") {
          const resumeAt = media.currentTime;
          const wasPlaying = !media.paused;
          media.addEventListener(
            "loadedmetadata",
            () => {
              media.currentTime = resumeAt;
              if (wasPlaying) {
                void media.play?.();
              }
            },
            { once: true },
          );
          media.setAttribute("src", next);
        }
      } catch {
        // Offline or the session ended: try again at the next check.
      } finally {
        renewing = false;
      }
    };

    /** Whether the token is old enough to renew. */
    const due = () => Date.now() - lastRenewal >= REFRESH_EVERY_MS;

    const check = setInterval(() => {
      if (!media.paused && due()) {
        void renew();
      }
    }, REFRESH_CHECK_MS);

    /** Renews before playback resumes on an old token. */
    const onPlay = () => {
      if (due()) {
        void renew();
      }
    };
    media.addEventListener("play", onPlay);

    return () => {
      active = false;
      clearInterval(check);
      media.removeEventListener("play", onPlay);
    };
  }, [mediaRef, mode, src, refreshSource]);

  return tokenRef;
}
