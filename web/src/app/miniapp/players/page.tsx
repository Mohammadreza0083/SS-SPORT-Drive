import type { Metadata } from "next";
import { MyPlayersScreen } from "./my-players-screen";

export const metadata: Metadata = {
  title: "My players · SS-SPORT Drive",
  robots: { index: false, follow: false },
};

/**
 * `/miniapp/players`: My players (T-7a), opened from the bot's My players button or the Mini App home.
 *
 * @returns The page.
 */
export default function MyPlayersPage() {
  return <MyPlayersScreen />;
}
