import { BrandLogo } from "@/components/viewer/brand-logo";
import { formatInitials } from "@/lib/admin/people";
import { sessionDaysLeft } from "@/lib/admin/format";
import type { SignedInAdmin } from "@/lib/admin/session";
import { adminCopy } from "../copy";

/** The sections of the console, in the order of the canvas's side navigation. */
export type AdminSection = "dashboard" | "players" | "waiting" | "requests" | "links" | "viewers" | "jobs" | "users" | "settings";

/** The counts the red badges show. */
export type AdminCounts = { waiting: number; accessRequests: number };

/** Props of {@link AdminNav}. */
type AdminNavProps = {
  /** The section that is open. */
  current: AdminSection;
  /** What waits on the admin; a badge shows only above zero. */
  counts: AdminCounts;
  /** Who is signed in. */
  admin: SignedInAdmin;
  /** The current time, for the session's days left. */
  now: Date;
};

/** One entry of the navigation. */
type NavEntry = { id: AdminSection; label: string; href: string; badge?: keyof AdminCounts };

/** The navigation, in the canvas's order. */
const ENTRIES: NavEntry[] = [
  { id: "dashboard", label: adminCopy.nav.dashboard, href: "/admin" },
  { id: "players", label: adminCopy.nav.players, href: "/admin/players" },
  { id: "waiting", label: adminCopy.nav.waiting, href: "/admin/waiting", badge: "waiting" },
  { id: "requests", label: adminCopy.nav.requests, href: "/admin/requests", badge: "accessRequests" },
  { id: "links", label: adminCopy.nav.links, href: "/admin/links" },
  { id: "viewers", label: adminCopy.nav.viewers, href: "/admin/viewers" },
  { id: "jobs", label: adminCopy.nav.jobs, href: "/admin/jobs" },
  { id: "users", label: adminCopy.nav.users, href: "/admin/users" },
  { id: "settings", label: adminCopy.nav.settings, href: "/admin/settings" },
];

/**
 * The console's side navigation (every admin board of the canvas shares it): the sections, the open one marked, red count
 * badges for the waiting list and access requests (hidden at zero), and the signed-in admin's card with the session's days
 * left and a Sign out button. Sign out is a POST form, so a link prefetch can never sign anyone out.
 *
 * @param props - See {@link AdminNavProps}.
 * @returns The navigation.
 */
export function AdminNav({ current, counts, admin, now }: AdminNavProps) {
  const name = admin.displayName ?? admin.email;
  return (
    <aside className="adm-aside">
      <a href="/admin" className="adm-logo">
        <BrandLogo height={34} alt={adminCopy.brand} priority />
      </a>
      <nav aria-label={adminCopy.nav.label} className="adm-nav">
        {ENTRIES.map((entry) => {
          const count = entry.badge ? counts[entry.badge] : 0;
          return (
            <a key={entry.id} href={entry.href} className="adm-nav-link" aria-current={entry.id === current ? "page" : undefined}>
              {entry.label}
              {count > 0 ? <span className="adm-badge">{count}</span> : null}
            </a>
          );
        })}
      </nav>
      <div className="adm-me">
        <div className="adm-me-row">
          <span className="adm-avatar" aria-hidden="true">
            {formatInitials(name)}
          </span>
          <span>
            <span className="adm-me-name">{name}</span>
            <span className="adm-me-role">{adminCopy.nav.role}</span>
          </span>
        </div>
        <div className="adm-me-session">{adminCopy.nav.session(sessionDaysLeft(admin.expiresAt, now))}</div>
        <form method="post" action="/api/admin/sign-out">
          <button type="submit" className="adm-signout">
            {adminCopy.nav.signOut}
          </button>
        </form>
      </div>
    </aside>
  );
}
