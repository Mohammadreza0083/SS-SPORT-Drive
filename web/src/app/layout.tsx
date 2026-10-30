import type { Metadata } from "next";
import { fontVariables } from "./fonts";
import "./globals.css";

export const metadata: Metadata = {
  title: "SS-SPORT Drive",
  description: "Volleyball player video pages by SS-SPORT.",
};

/**
 * Root layout shared by every route (public site, viewer pages, admin console, Telegram Mini App).
 *
 * @param props - The layout props provided by Next.js.
 * @returns The HTML document shell.
 */
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    // The viewer shell's intro script sets data-ss-intro on <html> before hydration (see lib/viewer/intro),
    // and browser extensions (e.g. ColorZilla's cz-shortcut-listen) add attributes to <body>. Both flags
    // ignore attribute differences on these two tags only, never on the page content inside them.
    <html lang="en" className={fontVariables} suppressHydrationWarning>
      <body suppressHydrationWarning>{children}</body>
    </html>
  );
}
