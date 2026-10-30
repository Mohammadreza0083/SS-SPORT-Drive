import { notFound } from "next/navigation";
import { PlayerPage } from "@/components/viewer/player-page/player-page";
import { samplePlayerPage } from "@/components/viewer/player-page/sample";

/**
 * Development-only preview of the player page (V-2) with the design canvas's sample player, for comparing
 * against the canvas and for the Playwright visual tests. Real pages arrive on personal links with L-1.
 * Production builds answer 404.
 *
 * @returns The preview page.
 */
export default function PlayerPagePreview() {
  if (process.env.NODE_ENV === "production") {
    notFound();
  }

  return <PlayerPage data={samplePlayerPage} />;
}
