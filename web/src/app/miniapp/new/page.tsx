import type { Metadata } from "next";
import { NewPlayerScreen } from "./new-player-screen";

export const metadata: Metadata = {
  title: "New player · SS-SPORT Drive",
  robots: { index: false, follow: false },
};

/**
 * `/miniapp/new`: the new-player form (T-4a).
 *
 * @returns The page.
 */
export default function NewPlayerPage() {
  return <NewPlayerScreen />;
}
