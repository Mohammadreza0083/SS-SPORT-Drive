import type { Metadata } from "next";
import { PlayerDetailScreen } from "../my-players-screen";

export const metadata: Metadata = {
  title: "Player · SS-SPORT Drive",
  robots: { index: false, follow: false },
};

/**
 * `/miniapp/players/{id}`: one player's detail (T-7a).
 *
 * @param props - The route params with the player id.
 * @returns The page.
 */
export default async function PlayerDetailPage({ params }: PageProps<"/miniapp/players/[id]">) {
  return <PlayerDetailScreen playerId={(await params).id} />;
}
