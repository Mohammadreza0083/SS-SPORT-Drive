"use client";

import { useEffect, type RefObject } from "react";
import { createWatchTransport } from "@/lib/viewer/tracking-client";
import { WatchTracker } from "@/lib/viewer/watch-tracker";

/** What {@link useWatchTracking} needs. */
type WatchTrackingOptions = {
  /** The media element the player drives. */
  mediaRef: RefObject<(HTMLElement & { currentTime?: number; ended?: boolean }) | null>;
  /** True once the media element is mounted and set up. */
  ready: boolean;
  /** The video's same-origin watch route; no tracking without it (the design preview). */
  watchPath: string | undefined;
};

/**
 * Reports a video's watching to the API (backlog W-2): the first play starts the watch, a heartbeat goes every
 * interval with the range played on the video's own clock, and pause, seek and end close the open range first. Leaving
 * the video (another one picked, or the page closed) sends the last range. Tracking never breaks playback.
 *
 * @param options - See {@link WatchTrackingOptions}.
 */
export function useWatchTracking({ mediaRef, ready, watchPath }: WatchTrackingOptions): void {
  useEffect(() => {
    const media = mediaRef.current;
    if (!ready || !media || !watchPath) {
      return;
    }
    const tracker = new WatchTracker(createWatchTransport(watchPath));
    const now = () => Number(media.currentTime) || 0;

    const onPlay = () => void tracker.play(now());
    const onTimeUpdate = () => tracker.timeUpdate(now());
    // Browsers fire `pause` just before `ended`; the end is reported once, as `Ended`.
    const onPause = () => {
      if (!media.ended) {
        void tracker.pause(now());
      }
    };
    // `seeking` already holds the new position; a `timeupdate` at it arrives before `seeked`.
    const onSeeking = () => void tracker.seeked(now());
    const onEnded = () => void tracker.ended(now());
    const onPageHide = () => void tracker.dispose();

    media.addEventListener("play", onPlay);
    media.addEventListener("timeupdate", onTimeUpdate);
    media.addEventListener("pause", onPause);
    media.addEventListener("seeking", onSeeking);
    media.addEventListener("ended", onEnded);
    window.addEventListener("pagehide", onPageHide);
    // The interval is known once the start has answered; ticking early only finds nothing to send.
    const timer = setInterval(() => void tracker.tick(), tracker.heartbeatSeconds * 1000);

    return () => {
      clearInterval(timer);
      media.removeEventListener("play", onPlay);
      media.removeEventListener("timeupdate", onTimeUpdate);
      media.removeEventListener("pause", onPause);
      media.removeEventListener("seeking", onSeeking);
      media.removeEventListener("ended", onEnded);
      window.removeEventListener("pagehide", onPageHide);
      void tracker.dispose();
    };
  }, [mediaRef, ready, watchPath]);
}
