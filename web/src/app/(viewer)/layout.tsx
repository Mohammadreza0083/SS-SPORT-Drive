import { introScript } from "@/lib/viewer/intro";
import "@/styles/tokens.css";
import "@/styles/motion.css";
import "@/styles/shell.css";
import "@/styles/player.css";
import "@/styles/player-page.css";
import "@/styles/document-viewer.css";

/**
 * Layout of every viewer-facing page (player pages, search, email gate, roster, sign-in): the dark brand
 * shell with its tokens and motion system. The Telegram Mini App and the admin console live outside it.
 *
 * The inline script runs before the page paints and picks the full intro or the returning viewer's
 * 400 ms fade, so the animation never starts one way and switches to the other.
 *
 * @param props - The layout props provided by Next.js.
 * @returns The viewer shell.
 */
export default function ViewerLayout({ children }: LayoutProps<"/">) {
  return (
    <>
      <script dangerouslySetInnerHTML={{ __html: introScript() }} />
      <div className="viewer">{children}</div>
    </>
  );
}
