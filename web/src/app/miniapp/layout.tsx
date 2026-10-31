import { TelegramProvider } from "./telegram";
import "@/styles/miniapp.css";

/**
 * Layout of every Mini App page: loads Telegram's script once and wraps the pages in the themed root (T-9), whose
 * colours follow the Telegram theme with SS-SPORT red for actions.
 *
 * @param props - The layout props provided by Next.js.
 * @returns The Mini App pages.
 */
export default function MiniAppLayout({ children }: LayoutProps<"/miniapp">) {
  return (
    <TelegramProvider>
      <div className="ma">{children}</div>
    </TelegramProvider>
  );
}
