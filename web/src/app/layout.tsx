import type { Metadata } from "next";
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
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
