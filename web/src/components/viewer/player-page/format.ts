import type { PlayerPageData, PlayerVideo } from "./types";
import { pageCopy } from "./copy";

/**
 * Writes a length as the canvas shows it: `m:ss`, or `h:mm:ss` from one hour.
 *
 * @param totalSeconds - The length in seconds.
 * @returns For example `1:24` or `1:48:10`.
 */
export function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.round(totalSeconds));
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  const ss = String(s).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${ss}` : `${m}:${ss}`;
}

/**
 * The filter chips above the playlist: "All", then each tag once, in the order the videos show them.
 *
 * @param videos - The page's videos.
 * @returns The chip labels.
 */
export function videoTags(videos: PlayerVideo[]): string[] {
  return [pageCopy.allTag, ...new Set(videos.map((video) => video.tag))];
}

/**
 * The phrases the red ticker scrolls: name, position, country, video count, a full-match line when there is
 * one, and the brand.
 *
 * @param data - The page data.
 * @returns The ticker phrases.
 */
export function tickerItems(data: PlayerPageData): string[] {
  const hasFullMatch = data.videos.some((video) => video.tag.toLowerCase() === pageCopy.fullMatchTag.toLowerCase());
  return [
    data.player.name,
    data.player.position.label,
    data.player.nationality.name,
    pageCopy.videoCount(data.stats.videos),
    ...(hasFullMatch ? [pageCopy.fullMatchIncluded] : []),
    pageCopy.brand,
  ];
}
