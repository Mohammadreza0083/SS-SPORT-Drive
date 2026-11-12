"use client";

import { useId, useState } from "react";
import { Chip } from "@/components/viewer/chip";
import { VideoPlayer } from "@/components/viewer/player/video-player";
import { useSignedSource } from "@/components/viewer/player/use-signed-source";
import { pageCopy } from "./copy";
import { formatDuration, videoTags } from "./format";
import type { PlayerVideo } from "./types";

/** Tints for thumbnails that are not rendered yet, taken from the canvas, one per tag. */
const PLACEHOLDER_TINTS: Record<string, string> = {
  highlight: "#3a1010,#120808",
  spike: "#21324a,#0d141d",
  serve: "#1e3a33,#0a1411",
  block: "#2e2240,#0e0b14",
  "full match": "#26262b,#0c0c0f",
};

/** Props of {@link VideosSection}. */
type VideosSectionProps = {
  /** The page's videos, in display order. */
  videos: PlayerVideo[];
  /** The signed-in viewer's email, laid over the player (V-4). */
  viewerEmail?: string;
};

/**
 * The Videos section: tag chips, the player and the playlist. Desktop shows the playlist beside the
 * player, tablet as a three-column grid under it, and phones as a list with horizontally scrolling chips.
 * Picking a video plays it straight away; the current item carries an equaliser.
 *
 * @param props - See {@link VideosSectionProps}.
 * @returns The section.
 */
export function VideosSection({ videos, viewerEmail }: VideosSectionProps) {
  const headingId = useId();
  const [tag, setTag] = useState<string>(pageCopy.allTag);
  const [selectedId, setSelectedId] = useState(videos[0]?.id);
  const [autoPlay, setAutoPlay] = useState(false);

  const current = videos.find((video) => video.id === selectedId) ?? videos[0];
  const shown = tag === pageCopy.allTag ? videos : videos.filter((video) => video.tag === tag);
  // Only the video on show is signed (V-5b), and again every 20 minutes while it plays; never the whole playlist.
  const source = useSignedSource(current?.streamPath, current?.src ?? "");

  if (!current) {
    return null;
  }

  return (
    <section id="videos" className="pp-videos" aria-labelledby={headingId}>
      <div className="pp-videos-head ss-sr">
        <h2 id={headingId} className="ss-h2">
          {pageCopy.videos}
        </h2>
        <div className="pp-chips" role="group" aria-label={pageCopy.filterVideos}>
          {videoTags(videos).map((label) => (
            <Chip key={label} small pressed={label === tag} onClick={() => setTag(label)}>
              {label}
            </Chip>
          ))}
        </div>
      </div>

      <div className="pp-videos-body">
        <div className="pp-videos-player ss-sr">
          {source.failed ? (
            <p className="pp-unavailable" role="alert">
              {pageCopy.videoUnavailable}
            </p>
          ) : (
            <VideoPlayer
              src={source.src}
              title={current.title}
              tag={current.tag}
              fps={current.fps}
              autoPlay={autoPlay}
              viewerEmail={viewerEmail}
              refreshSource={source.refreshSource}
            />
          )}
        </div>

        <ul className="pp-playlist ss-sr" aria-label={pageCopy.playlist}>
          {shown.map((video) => {
            const active = video.id === current.id;
            return (
              <li key={video.id}>
                <button
                  type="button"
                  className={active ? "pp-item ss-item is-active" : "pp-item ss-item"}
                  aria-current={active ? "true" : undefined}
                  onClick={() => {
                    setSelectedId(video.id);
                    setAutoPlay(true);
                  }}
                >
                  <span className="pp-thumb">
                    <span
                      className="pp-thumb-img ss-thz"
                      style={
                        video.thumbnailUrl
                          ? { backgroundImage: `url(${video.thumbnailUrl})` }
                          : { backgroundImage: `linear-gradient(135deg,${PLACEHOLDER_TINTS[video.tag.toLowerCase()] ?? "#1a1d24,#0b0c10"})` }
                      }
                    />
                    <span className="pp-duration">{formatDuration(video.durationSeconds)}</span>
                  </span>
                  <span className="pp-item-text">
                    <span className="pp-item-title">{video.title}</span>
                    <span className="pp-item-meta">
                      <span className="pp-item-tag">{video.tag}</span>
                      {active ? (
                        <span className="pp-eq" role="img" aria-label={pageCopy.nowPlaying}>
                          <span className="ss-eqb" />
                          <span className="ss-eqb" style={{ animationDelay: ".2s" }} />
                          <span className="ss-eqb" style={{ animationDelay: ".4s" }} />
                        </span>
                      ) : null}
                    </span>
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      </div>
    </section>
  );
}
