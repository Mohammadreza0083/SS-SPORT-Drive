import { notFound } from "next/navigation";
import { SiteNav } from "@/components/viewer/site-nav";
import { VideoPlayer } from "@/components/viewer/player/video-player";

/**
 * A public multi-rendition HLS test stream (Mux), used only on this development preview until ingest
 * (EPIC 3) produces real player videos.
 */
const TEST_STREAM = "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8";

/**
 * Development-only preview of the video player (V-3). Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function PlayerPreviewPage() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return (
    <>
      <SiteNav />
      <section className="ss-container" style={{ padding: "30px var(--ss-gutter) 80px", maxWidth: 900 }}>
        <VideoPlayer src={TEST_STREAM} title="Highlights 2026" tag="Highlight" fps={24} />
      </section>
    </>
  );
}
