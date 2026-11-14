import type { Metadata } from "next";
import "@/styles/tokens.css";
import "@/styles/motion.css";
import "@/styles/shell.css";
import "@/styles/gate.css";

export const metadata: Metadata = {
  title: { default: "Admin · SS-SPORT Drive", template: "%s · SS-SPORT Admin" },
  robots: { index: false, follow: false },
};

/**
 * Layout of the admin pages: the sign-in page keeps the viewer site's dark brand look (W-1); the console inside it adds
 * its own styles (Epic 7).
 *
 * @param props - The layout props provided by Next.js.
 * @returns The admin shell.
 */
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return <div className="viewer">{children}</div>;
}
