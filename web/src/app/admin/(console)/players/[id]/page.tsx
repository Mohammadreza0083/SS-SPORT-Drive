import type { Metadata } from "next";
import Link from "next/link";
import { adminCopy } from "@/components/admin/copy";
import { NewLinkLauncher } from "@/components/admin/links/new-link-launcher";
import { PlayerFiles } from "@/components/admin/players/player-files";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatDate, formatDateTime } from "@/lib/admin/people";
import { formatPercent, formatRelative, formatWatchTime } from "@/lib/admin/format";
import { adminGet, firstParam, requireAdmin } from "@/lib/admin/server";
import type { LinkRow, Paged, PlayerDetail, PlayerFile, SettingsData, ViewerRow } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.players.title };

/**
 * `/admin/players/{id}`: one player (A-4, canvas `AdminPlayer`): the header facts, the files tab (order, hero, visibility,
 * retry), "Who has access", the Drive folders and the share links. `?tab=access` picks the second tab; `?newLink=1` opens
 * the New share link modal (A-6), which the waiting list's done state links to.
 *
 * @param props - The page props.
 * @returns The page.
 */
export default async function PlayerPage({ params, searchParams }: PageProps<"/admin/players/[id]">) {
  const { id } = await params;
  const query = await searchParams;
  const tab = firstParam(query, "tab") === "access" ? "access" : "files";
  const { token } = await requireAdmin();
  const [player, files, links, settings] = await Promise.all([
    adminGet<PlayerDetail>(token, `/admin/players/${id}`),
    adminGet<Paged<PlayerFile>>(token, `/admin/players/${id}/files?pageSize=100`),
    adminGet<Paged<LinkRow>>(token, `/admin/links?playerId=${id}&pageSize=5`),
    adminGet<SettingsData>(token, "/admin/settings"),
  ]);
  const viewers = tab === "access" ? await adminGet<Paged<ViewerRow>>(token, `/admin/viewers?playerId=${id}&pageSize=50`) : null;
  const copy = adminCopy.player;
  const now = new Date();
  const name = player.name ?? adminCopy.players.unnamed;

  return (
    <>
      <Link href="/admin/players">{copy.back}</Link>
      <header className="adm-head" style={{ marginTop: 14 }}>
        <div className="adm-inline">
          <span className="adm-face" style={{ width: 64, height: 64 }} />
          <div>
            <h1 className="adm-title" style={{ marginTop: 0 }}>
              {name}
            </h1>
            <p className="adm-lead">
              {player.positions.join(" · ")} · {player.nationality?.name ?? "—"}
              {player.volleyboxId ? <> · Volleybox ID <span className="adm-mono">{player.volleyboxId}</span></> : null} · {player.analystName ?? "—"} · {formatDate(player.createdAt)}
            </p>
            <div className="adm-chips">
              <StatusBadge status={player.status} />
              <span className="adm-pill">{copy.viewersWithAccess(player.viewersWithAccess)}</span>
              <span className="adm-pill">{copy.avgWatched(formatPercent(player.averageCompletion))}</span>
            </div>
          </div>
        </div>
        <div className="adm-actions-top">
          {player.slug ? (
            <a className="adm-btn" href={`/players/${player.slug}`} target="_blank" rel="noreferrer">
              {copy.openPage}
            </a>
          ) : null}
          {player.status !== "Waiting" ? (
            <NewLinkLauncher player={{ id: player.id, name }} defaultExpiryDays={settings.defaultLinkExpiryDays} openOnLoad={firstParam(query, "newLink") === "1"} />
          ) : (
            <a className="adm-btn adm-btn-red" href="/admin/waiting">
              {adminCopy.nav.waiting}
            </a>
          )}
        </div>
      </header>

      <div className="adm-tabs" role="tablist" aria-label="Player sections">
        <a role="tab" className="adm-tab" aria-selected={tab === "files"} href={`/admin/players/${id}`}>
          {copy.tabs.files}
        </a>
        <a role="tab" className="adm-tab" aria-selected={tab === "access"} href={`/admin/players/${id}?tab=access`}>
          {copy.tabs.access} · {player.viewersWithAccess}
        </a>
      </div>

      <div className="adm-split">
        <section className="adm-table" style={{ marginTop: 0 }} aria-label={tab === "files" ? copy.tabs.files : copy.tabs.access}>
          {tab === "files" ? (
            <PlayerFiles playerId={id} files={files.items} />
          ) : viewers && viewers.items.length > 0 ? (
            <div className="adm-scroll">
              <div style={{ minWidth: 760 }}>
                <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "2fr .8fr .8fr 1fr 1fr" }}>
                  {["Viewer", "Plays", "Watched", "Last seen", "Until"].map((column) => (
                    <span key={column}>{column}</span>
                  ))}
                </div>
                {viewers.items.map((viewer) => (
                  <a key={viewer.id} href={`/admin/viewers/${viewer.id}`} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "2fr .8fr .8fr 1fr 1fr" }}>
                    <span>{viewer.email}</span>
                    <span className="adm-mono">{viewer.plays}</span>
                    <span className="adm-mono">{formatWatchTime(viewer.watchSeconds)}</span>
                    <span className="adm-muted">{formatRelative(viewer.lastSeenAt, now)}</span>
                    <span className="adm-muted">{formatDate(viewer.accessUntil)}</span>
                  </a>
                ))}
              </div>
            </div>
          ) : (
            <div className="adm-empty">{copy.noAccess}</div>
          )}
        </section>

        <aside style={{ flex: "1 1 320px", display: "grid", gap: 14, minWidth: 0 }}>
          <section className="adm-card">
            <div className="adm-label">{copy.folder}</div>
            {player.folders.length === 0 ? <p className="adm-muted">{copy.noFolder}</p> : null}
            {player.folders.map((folder) => (
              <div key={folder.id} style={{ marginTop: 10 }}>
                <div>{folder.name}</div>
                <div className="adm-sub">
                  {folder.access === "Lost" ? copy.folderLost : copy.synced(formatRelative(folder.lastSyncedAt, now))}
                </div>
              </div>
            ))}
          </section>
          <section className="adm-card">
            <div className="adm-label">{copy.links}</div>
            {links.items.length === 0 ? <p className="adm-muted">{copy.noLinks}</p> : null}
            {links.items.map((link) => (
              <a key={link.id} href="/admin/links" className="adm-attn" style={{ display: "block" }}>
                <span>
                  {link.recipientOrganisation ? `${link.recipientOrganisation} · ` : ""}
                  {link.recipientName}
                </span>
                <span className="adm-sub">
                  <span className="adm-mono">/r/{link.code}</span> · {link.access} · {link.expiresAt ? formatDateTime(link.expiresAt) : "—"}
                </span>
              </a>
            ))}
          </section>
        </aside>
      </div>
    </>
  );
}
