"use client";

import { useEffect, useId, useRef, useState, useSyncExternalStore, type KeyboardEvent, type MouseEvent } from "react";
import {
  MediaControlBar,
  MediaController,
  MediaFullscreenButton,
  MediaLoadingIndicator,
  MediaMuteButton,
  MediaPlayButton,
  MediaTimeDisplay,
  MediaTimeRange,
  MediaVolumeRange,
} from "media-chrome/react";
import {
  MediaPlaybackRateMenu,
  MediaPlaybackRateMenuButton,
  MediaRenditionMenu,
  MediaRenditionMenuButton,
} from "media-chrome/react/menu";
import {
  DEFAULT_FPS,
  HLS_CONFIG,
  PLAYBACK_RATES,
  detectHlsSupport,
  frameDirection,
  frameStep,
  playbackMode,
  type PlaybackMode,
} from "@/lib/viewer/player";
import { playerCopy } from "./copy";
import { EmailOverlay } from "./email-overlay";
import { useHlsRefresh } from "./use-hls-refresh";
import { swapToken } from "@/lib/viewer/hls-token";

/** Props of {@link VideoPlayer}. */
type VideoPlayerProps = {
  /** The HLS master playlist (signed by the edge once V-5 lands). */
  src: string;
  /** The video's title, shown under the player and used to name the player for screen readers. */
  title: string;
  /** The video's tag (Highlight, Spike, Full match, …), shown as a red label beside the title. */
  tag?: string;
  /** A still image shown before playback starts. */
  poster?: string;
  /** The video's frame rate, for frame stepping. */
  fps?: number;
  /** Starts playing as soon as it can; used when a viewer picks a video from the playlist. */
  autoPlay?: boolean;
  /** The signed-in viewer's email, laid faintly over the video (V-4); no overlay without it. */
  viewerEmail?: string;
  /** Fetches a freshly signed playlist URL for this video; renews the 30-minute edge token while watching (V-5a). */
  refreshSource?: () => Promise<string>;
};

/** The media element the player drives: a native `<video>` or the `<hls-video>` custom element. */
type PlayerMedia = HTMLElement & { currentTime: number; duration: number; pause?: () => void; config?: unknown; src?: string };

/** Nothing changes HLS support while the page is open, so there is nothing to subscribe to. */
const noSubscription = () => () => {};

/**
 * The branded video player (backlog V-3): Media Chrome controls over hls.js (or the browser's own HLS on
 * iPhone and iPad), speed 0.25×–2×, quality, frame stepping with buttons and the `,` `.` keys, fullscreen.
 * There is no download anywhere: no button, no context menu, no picture-in-picture or casting, and the
 * native download control is switched off. Only HLS playlists are ever loaded. With a viewer email the
 * moving email overlay (V-4) sits over the video, and fullscreen takes the whole frame so it stays visible.
 *
 * @param props - See {@link VideoPlayerProps}.
 * @returns The player with its title row.
 */
export function VideoPlayer({ src, title, tag, poster, fps = DEFAULT_FPS, autoPlay = false, viewerEmail, refreshSource }: VideoPlayerProps) {
  const id = useId();
  const mediaRef = useRef<PlayerMedia | null>(null);
  const frameRef = useRef<HTMLDivElement>(null);
  const controllerRef = useRef<HTMLElement & { fullscreenElement?: HTMLElement }>(null);
  const mode = useSyncExternalStore<PlaybackMode | null>(noSubscription, () => playbackMode(detectHlsSupport()), () => null);
  const [hlsReady, setHlsReady] = useState(false);

  useEffect(() => {
    if (mode !== "hls") {
      return;
    }
    let active = true;
    // Loads hls.js (bundled in hls-video-element) only for browsers that need it.
    import("hls-video-element").then(() => {
      if (active) {
        setHlsReady(true);
      }
    });
    return () => {
      active = false;
    };
  }, [mode]);

  const ready = mode === "native" || (mode === "hls" && hlsReady);
  const tokenRef = useHlsRefresh({ mediaRef, mode: ready ? mode : null, src, refreshSource });

  useEffect(() => {
    // Fullscreen takes the frame (controller + email overlay), not just the controller.
    if (controllerRef.current && frameRef.current) {
      controllerRef.current.fullscreenElement = frameRef.current;
    }
  }, [ready]);

  useEffect(() => {
    const media = mediaRef.current;
    if (!ready || !media) {
      return;
    }
    media.setAttribute("playsinline", "");
    media.setAttribute("disablepictureinpicture", "");
    media.setAttribute("disableremoteplayback", "");
    media.setAttribute("controlslist", "nodownload noremoteplayback noplaybackrate");
    media.setAttribute("preload", "metadata");
    media.toggleAttribute("autoplay", autoPlay);
    if (mode === "hls") {
      // The config must be in place before the source, so hls.js starts at 360p. Every request carries the latest
      // edge token, so a renewal (V-5a) takes effect without restarting the stream.
      media.config = {
        ...HLS_CONFIG,
        xhrSetup: (xhr: XMLHttpRequest, url: string) => {
          const next = swapToken(url, tokenRef.current);
          if (next !== url) {
            xhr.open("GET", next, true);
          }
        },
      };
    }
    media.setAttribute("src", src);
  }, [ready, mode, src, autoPlay, tokenRef]);

  /**
   * Pauses and moves one frame forward or back.
   *
   * @param direction - 1 for the next frame, -1 for the previous one.
   */
  const step = (direction: 1 | -1) => {
    const media = mediaRef.current;
    if (!media) {
      return;
    }
    media.pause?.();
    media.currentTime = frameStep(media.currentTime, media.duration, direction, fps);
  };

  /**
   * Handles the frame-step keys while focus is inside the player.
   *
   * @param event - The key press.
   */
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const direction = frameDirection(event.key);
    if (direction) {
      event.preventDefault();
      step(direction);
    }
  };

  /**
   * Blocks the browser's context menu (no "Save video as…").
   *
   * @param event - The right click.
   */
  const blockMenu = (event: MouseEvent) => event.preventDefault();

  const rateMenu = `${id}-rate`;
  const qualityMenu = `${id}-quality`;

  return (
    <div className="ss-player">
      <div
        role="region"
        aria-label={playerCopy.region(title)}
        className="ss-player-frame"
        ref={frameRef}
        onKeyDown={onKeyDown}
        onContextMenu={blockMenu}
      >
        {/*
          The server and the hydrating render send only the empty stage (`mode` is null until hydration ends): Media
          Chrome renders declarative shadow DOM, which the browser drops when a page streams in after the custom
          elements are defined (every `/r/{code}` page), and hydration then fails. The controls mount right after.
        */}
        {mode === null ? (
          <div className="ss-player-stage" />
        ) : (
          <MediaController ref={controllerRef as never} className="ss-player-stage" defaultStreamType="on-demand">
            {ready && mode === "hls" ? (
              <hls-video ref={(el: PlayerMedia | null) => void (mediaRef.current = el)} slot="media" poster={poster} />
            ) : null}
            {ready && mode === "native" ? (
              <video ref={(el) => void (mediaRef.current = el as PlayerMedia | null)} slot="media" poster={poster} />
            ) : null}
            {mode === "unsupported" ? <p className="ss-player-unsupported">{playerCopy.unsupported}</p> : null}

            <MediaLoadingIndicator slot="centered-chrome" className="ss-player-loading" noAutohide />
            <MediaPlayButton slot="centered-chrome" className="ss-player-big" />

            {/* Menus sit above the control bar and open over the video. */}
            <MediaPlaybackRateMenu id={rateMenu} className="ss-player-menu" hidden anchor="auto" rates={[...PLAYBACK_RATES]} />
            <MediaRenditionMenu id={qualityMenu} className="ss-player-menu" hidden anchor="auto" />
            <MediaControlBar className="ss-player-bar">
              <MediaTimeRange className="ss-player-progress" />
              <div className="ss-player-row">
                <MediaPlayButton className="ss-player-btn" />
                <MediaTimeDisplay className="ss-player-time" showDuration />
                <span className="ss-player-spacer" />
                <span className="ss-player-label">{playerCopy.frameByFrame}</span>
                <button type="button" className="ss-player-btn ss-player-frame-btn" aria-label={playerCopy.previousFrame} onClick={() => step(-1)}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path d="M6 5h2v14H6zM20 5v14l-10-7z" />
                  </svg>
                </button>
                <button type="button" className="ss-player-btn ss-player-frame-btn" aria-label={playerCopy.nextFrame} onClick={() => step(1)}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path d="M16 5h2v14h-2zM4 5v14l10-7z" />
                  </svg>
                </button>
                <MediaPlaybackRateMenuButton className="ss-player-chip" invokeTarget={rateMenu} />
                <MediaRenditionMenuButton className="ss-player-chip" invokeTarget={qualityMenu} />
                <MediaMuteButton className="ss-player-btn ss-player-mute" />
                <MediaVolumeRange className="ss-player-volume" />
                <MediaFullscreenButton className="ss-player-btn" />
              </div>
            </MediaControlBar>

          </MediaController>
        )}
        {viewerEmail ? <EmailOverlay email={viewerEmail} /> : null}
      </div>

      <div className="ss-player-meta">
        <h3 className="ss-player-title">{title}</h3>
        {tag ? <span className="ss-player-tag">{tag}</span> : null}
        <span className="ss-player-note">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <rect x="5" y="11" width="14" height="10" rx="2" />
            <path d="M8 11V8a4 4 0 0 1 8 0v3" />
          </svg>
          {playerCopy.protectedStream}
        </span>
      </div>
    </div>
  );
}
