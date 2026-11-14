"use client";

import { usePathname } from "next/navigation";
import type { SignedInAdmin } from "@/lib/admin/session";
import { AdminNav, type AdminCounts, type AdminSection } from "./admin-nav";

/** The section each address belongs to, longest prefix first. */
const SECTIONS: [string, AdminSection][] = [
  ["/admin/players", "players"],
  ["/admin/waiting", "waiting"],
  ["/admin/requests", "requests"],
  ["/admin/links", "links"],
  ["/admin/viewers", "viewers"],
  ["/admin/jobs", "jobs"],
  ["/admin/users", "users"],
  ["/admin/settings", "settings"],
];

/**
 * Finds the section an address belongs to.
 *
 * @param pathname - The current path.
 * @returns The section; the dashboard for `/admin` itself.
 */
export function sectionOf(pathname: string): AdminSection {
  return SECTIONS.find(([prefix]) => pathname === prefix || pathname.startsWith(`${prefix}/`))?.[1] ?? "dashboard";
}

/** Props of {@link ConsoleNav}. */
type ConsoleNavProps = {
  /** What waits on the admin. */
  counts: AdminCounts;
  /** Who is signed in. */
  admin: SignedInAdmin;
  /** The server's current time (ISO 8601), for the session's days left. */
  nowIso: string;
};

/**
 * The side navigation with the open section taken from the address, so the server layout can stay one for every page.
 *
 * @param props - See {@link ConsoleNavProps}.
 * @returns The navigation.
 */
export function ConsoleNav({ counts, admin, nowIso }: ConsoleNavProps) {
  return <AdminNav current={sectionOf(usePathname())} counts={counts} admin={admin} now={new Date(nowIso)} />;
}
