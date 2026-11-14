import type { Metadata } from "next";
import "@/styles/tokens.css";
import "@/styles/admin.css";

export const metadata: Metadata = {
  title: { default: "Admin · SS-SPORT Drive", template: "%s · SS-SPORT Admin" },
  robots: { index: false, follow: false },
};

/**
 * Layout of every admin page (sign-in and the console): the dark brand shell with the console's styles. The Telegram
 * Mini App and the viewer site live outside it.
 *
 * @param props - The layout props provided by Next.js.
 * @returns The admin shell.
 */
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return <div className="admin">{children}</div>;
}
