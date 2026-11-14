import type { ReactNode } from "react";
import { Suspense } from "react";
import { ConsoleNav } from "@/components/admin/shell/console-nav";
import { requireAdmin, adminGet } from "@/lib/admin/server";
import type { DashboardData, WaitingSummary } from "@/lib/admin/types";

/**
 * Layout of every console page: the session is checked on the server (no session goes to the sign-in page), then the
 * side navigation with the waiting and access-request counts wraps the page. The session cookie is request data, so
 * everything is read inside `<Suspense>`.
 *
 * @param props - The layout props provided by Next.js.
 * @param props.children - The page.
 * @returns The console frame.
 */
export default function ConsoleLayout({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={null}>
      <Console>{children}</Console>
    </Suspense>
  );
}

/**
 * Reads who is signed in and what waits on them.
 *
 * @param props - The props.
 * @param props.children - The page.
 * @returns The navigation and the page.
 */
async function Console({ children }: { children: ReactNode }) {
  const { admin, token } = await requireAdmin();
  const [waiting, dashboard] = await Promise.all([
    adminGet<WaitingSummary>(token, "/admin/waiting/summary"),
    adminGet<DashboardData>(token, "/admin/dashboard?period=7d"),
  ]);
  return (
    <>
      <ConsoleNav counts={{ waiting: waiting.waiting, accessRequests: dashboard.kpis.pendingAccessRequests }} admin={admin} nowIso={new Date().toISOString()} />
      <main className="adm-main">{children}</main>
    </>
  );
}
