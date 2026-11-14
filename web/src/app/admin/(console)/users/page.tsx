import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { UserActions } from "@/components/admin/users/user-actions";
import { formatRelative } from "@/lib/admin/format";
import { formatDateTime } from "@/lib/admin/people";
import { adminGet, firstParam, requireAdmin } from "@/lib/admin/server";
import type { AuditRow, Cursored, Paged, UserRow } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.users.title };

/** The columns of the team table. */
const COLUMNS = "1.8fr 1.8fr .8fr .7fr 1fr .9fr 190px";

/**
 * `/admin/users`: users and roles with the audit log (A-8, canvas `AdminUsers`): pending bot requests to approve or
 * decline, the team with their role and sign-in, what each role may do, and the append-only audit log, newest first
 * (`?cursor=` pages it).
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function UsersPage({ searchParams }: PageProps<"/admin/users">) {
  const params = await searchParams;
  const cursor = firstParam(params, "cursor");
  const { token } = await requireAdmin();
  const [pending, team, audit] = await Promise.all([
    adminGet<Paged<UserRow>>(token, "/admin/users?status=pending&pageSize=50"),
    adminGet<Paged<UserRow>>(token, "/admin/users?status=active&pageSize=100"),
    adminGet<Cursored<AuditRow>>(token, `/admin/audit?pageSize=15${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
  ]);
  const copy = adminCopy.users;
  const now = new Date();
  const admins = team.items.filter((user) => user.role === "Admin").map((user) => ({ id: user.id, name: user.displayName }));
  return (
    <>
      <PageHeader title={copy.title} lead={copy.lead} />
      {pending.items.map((user) => (
        <div key={user.id} className="adm-card" style={{ marginTop: 16, display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap", borderColor: "#5a3a0a" }}>
          <span className="adm-pill adm-pill-amber">{copy.pending}</span>
          <span style={{ flex: 1 }}>{copy.pendingLine(user.displayName, user.username)}</span>
          <UserActions user={user} admins={admins} />
        </div>
      ))}

      <div className="adm-wrap">
        <section className="adm-table" aria-label={copy.team} style={{ flex: "999 1 640px", marginTop: 0 }}>
          <div className="adm-scroll">
            <div style={{ minWidth: 880 }}>
              <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: COLUMNS }}>
                {copy.columns.map((column, index) => (
                  <span key={index}>{column}</span>
                ))}
              </div>
              {team.items.map((user) => (
                <div key={user.id} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: COLUMNS }}>
                  <span style={{ fontWeight: 600 }}>{user.displayName}</span>
                  <span className="adm-muted">{user.signIn === "Email" ? `${user.email ?? ""} · email link` : `@${user.username ?? "—"} · Telegram bot`}</span>
                  <span>
                    <StatusBadge status={user.role === "Admin" ? "Failed" : "Hidden"} label={user.role} />
                  </span>
                  <span className="adm-mono">{user.role === "Admin" ? "—" : user.playerCount}</span>
                  <span className="adm-muted">{formatRelative(user.lastActiveAt, now)}</span>
                  <span>
                    <StatusBadge status={user.status} />
                  </span>
                  <UserActions user={user} admins={admins} />
                </div>
              ))}
            </div>
          </div>
        </section>
        <section className="adm-card" aria-label={copy.permissions} style={{ flex: "1 1 320px" }}>
          <div className="adm-label">{copy.permissions}</div>
          {copy.permissionRows.map(([capability, analyst, admin]) => (
            <div key={capability} className="adm-attn" style={{ display: "grid", gridTemplateColumns: "1fr 80px 80px", gap: 8 }}>
              <span>{capability}</span>
              <span className="adm-muted">{analyst}</span>
              <span>{admin}</span>
            </div>
          ))}
        </section>
      </div>

      <section className="adm-card" aria-label={copy.audit} style={{ marginTop: 18 }}>
        <div className="adm-label">{copy.audit}</div>
        {audit.items.length === 0 ? <p className="adm-muted">{copy.noAudit}</p> : null}
        {audit.items.map((entry) => (
          <div key={entry.id} className="adm-attn">
            <span className="adm-mono adm-muted" style={{ minWidth: 110 }}>
              {formatDateTime(entry.at)}
            </span>
            <span>
              <strong>{entry.actorName ?? "System"}</strong> · <span className="adm-mono">{entry.action}</span> · {entry.targetType}
            </span>
          </div>
        ))}
        {audit.nextCursor ? (
          <a className="adm-btn adm-btn-small" href={`/admin/users?cursor=${encodeURIComponent(audit.nextCursor)}`} style={{ marginTop: 12 }}>
            {copy.loadMore}
          </a>
        ) : null}
      </section>
    </>
  );
}
