import "@/styles/tokens.css";
import "@/styles/motion.css";
import "@/styles/shell.css";
import "@/styles/gate.css";

/**
 * Layout of the admin pages (W-1): the same dark brand look as the viewer site until the console (Epic 7) has its own.
 *
 * @param props - The layout props provided by Next.js.
 * @returns The admin shell.
 */
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return <div className="viewer">{children}</div>;
}
