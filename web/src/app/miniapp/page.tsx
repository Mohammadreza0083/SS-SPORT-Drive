import type { Metadata } from "next";
import { MiniApp } from "./mini-app-home";

export const metadata: Metadata = {
  title: "SS-SPORT Drive",
  robots: { index: false, follow: false },
};

/**
 * `/miniapp`: the Telegram Mini App, opened from the bot's "Open Drive" menu button (T-3).
 *
 * @returns The Mini App page.
 */
export default function MiniAppPage() {
  return <MiniApp />;
}
