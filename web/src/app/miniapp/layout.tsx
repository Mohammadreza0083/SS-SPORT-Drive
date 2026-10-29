import { TelegramProvider } from "./telegram";

/**
 * Layout of every Mini App page: loads Telegram's script once.
 *
 * @param props - The layout props provided by Next.js.
 * @returns The Mini App pages.
 */
export default function MiniAppLayout({ children }: LayoutProps<"/miniapp">) {
  return <TelegramProvider>{children}</TelegramProvider>;
}
