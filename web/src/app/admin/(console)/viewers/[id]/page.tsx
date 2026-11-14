import type { Metadata } from "next";
import Link from "next/link";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { ReplayHeatmap } from "@/components/admin/viewer/replay-heatmap";
import { formatPercent, formatRelative, formatWatchTime } from "@/lib/admin/format";
import { formatDate, formatDateTime } from "@/lib/admin/people";
import { adminGet, firstParam, pageParam, requireAdmin } from "@/lib/admin/server";
import type { Cursored, Paged, ViewerAccessRow, ViewerHeatmap, ViewerProfile, ViewerSessionRow, ViewerVideoRow } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.viewer.tiles.plays };

/**
 * `/admin/viewers/{id}`: one viewer (A-7 profile and replay heatmap, A-11 access and watch log; canvas `AdminViewer`).
 * The viewer is addressed by id, never by email. `?player=` narrows the watch log, `?video=` shows that video's replay
 * heatmap and `?cursor=` pages the sessions.
 *
 * @param props - The page props.
 * @returns The page.
 */
export default async function ViewerPage({ params, searchParams }: PageProps<"/admin/viewers/[id]">) {
  const { id } = await params;
  const query = await searchParams;
  const player = firstParam(query, "player");
  const video = firstParam(query, "video");
  const cursor = firstParam(query, "cursor");
  const { token } = await requireAdmin();
  const log = new URLSearchParams({ page: String(pageParam(query)), pageSize: "50" });
  if (player) {
    log.set("playerId", player);
  }

  const sessionQuery = new URLSearchParams({ pageSize: "10" });
  if (cursor) {
    sessionQuery.set("cursor", cursor);
  }

  const [profile, access, videos, sessions, heatmap] = await Promise.all([
    adminGet<ViewerProfile>(token, `/admin/viewers/${id}`),
    adminGet<Paged<ViewerAccessRow>>(token, `/admin/viewers/${id}/access?pageSize=50`),
    adminGet<Paged<ViewerVideoRow>>(token, `/admin/viewers/${id}/videos?${log.toString()}`),
    adminGet<Cursored<ViewerSessionRow>>(token, `/admin/viewers/${id}/sessions?${sessionQuery.toString()}`),
    video ? adminGet<ViewerHeatmap>(token, `/admin/viewers/${id}/videos/${video}/heatmap`) : Promise.resolve(null),
  ]);
  const copy = adminCopy.viewer;
  const now = new Date();
  const playerNames = [...new Map(access.items.map((row) => [row.playerId, row.playerName])).entries()];
  const tiles = [
    { label: copy.tiles.plays, value: String(profile.plays) },
    { label: copy.tiles.watchTime, value: formatWatchTime(profile.watchSeconds) },
    { label: copy.tiles.players, value: String(profile.playersAccessible) },
    { label: copy.tiles.completion, value: formatPercent(profile.averageCompletion) },
    { label: copy.tiles.lastSeen, value: profile.lastSeenAt ? formatRelative(profile.lastSeenAt, now) : adminCopy.viewers.never },
  ];
  const filterHref = (value: string | null) => `/admin/viewers/${id}${value ? `?player=${value}` : ""}`;

  return (
    <>
      <Link href="/admin/viewers">{copy.back}</Link>
      <PageHeader title={profile.email} lead={`${profile.organisation ? `${profile.organisation} · ` : ""}${copy.firstSeen(formatDate(profile.firstSeenAt))}`} actions={<StatusBadge status={profile.account} label={adminCopy.viewers.accounts[profile.account]} />} />
      <div className="adm-kpis">
        {tiles.map((tile) => (
          <div key={tile.label} className="adm-kpi">
            <div className="adm-label">{tile.label}</div>
            <div className="adm-kpi-value" style={{ fontSize: 26 }}>
              {tile.value}
            </div>
          </div>
        ))}
      </div>

      <section className="adm-table" aria-label={copy.access}>
        <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "1.6fr 1fr 1fr 1fr .7fr .9fr" }}>
          {copy.accessColumns.map((column) => (
            <span key={column}>{column}</span>
          ))}
        </div>
        {access.items.map((row) => (
          <div key={`${row.linkId}-${row.playerId}`} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "1.6fr 1fr 1fr 1fr .7fr .9fr" }}>
            <a href={`/admin/players/${row.playerId}`}>{row.playerName}</a>
            <span className="adm-mono">/r/{row.code}</span>
            <StatusBadge status={row.status} />
            <span>{formatDate(row.expiresAt)}</span>
            <span className="adm-mono">{row.plays}</span>
            <span className="adm-mono">{formatWatchTime(row.watchSeconds)}</span>
          </div>
        ))}
      </section>

      <div className="adm-split">
        <section aria-label={copy.watchLog}>
          <div className="adm-chips" style={{ marginTop: 0 }}>
            <a className="adm-chip" href={filterHref(null)} aria-current={player ? undefined : "true"}>
              {copy.all}
            </a>
            {playerNames.map(([playerId, name]) => (
              <a key={playerId} className="adm-chip" href={filterHref(playerId)} aria-current={player === playerId ? "true" : undefined}>
                {name.split(" ")[0]}
              </a>
            ))}
          </div>
          <div className="adm-table">
            <div className="adm-scroll">
              <div style={{ minWidth: 760 }}>
                <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "1.1fr 1.6fr .6fr .8fr .9fr 1fr" }}>
                  {copy.logColumns.map((column) => (
                    <span key={column}>{column}</span>
                  ))}
                </div>
                {videos.items.map((row) => (
                  <a key={row.assetId} href={`/admin/viewers/${id}?${player ? `player=${player}&` : ""}video=${row.assetId}`} className="adm-grid adm-grid-row" aria-current={video === row.assetId ? "true" : undefined} style={{ gridTemplateColumns: "1.1fr 1.6fr .6fr .8fr .9fr 1fr", background: video === row.assetId ? "rgba(204,0,0,.1)" : undefined }}>
                    <span>{row.playerName}</span>
                    <span>{row.title}</span>
                    <span className="adm-mono">{row.plays}</span>
                    <span className="adm-mono">{formatWatchTime(row.watchSeconds)}</span>
                    <span className="adm-mono">{formatPercent(row.completion)}</span>
                    <span className="adm-muted">{formatRelative(row.lastWatchedAt, now)}</span>
                  </a>
                ))}
              </div>
            </div>
          </div>
          {heatmap ? <ReplayHeatmap title={heatmap.title} buckets={heatmap.buckets} bucketSeconds={heatmap.bucketSeconds} /> : <p className="adm-muted" style={{ marginTop: 14 }}>{copy.pickVideo}</p>}
        </section>
        <section className="adm-card" aria-label={copy.sessions} style={{ flex: "1 1 320px" }}>
          <div className="adm-label">{copy.sessions}</div>
          {sessions.items.length === 0 ? <p className="adm-muted">{copy.noSessions}</p> : null}
          {sessions.items.map((row) => (
            <div key={row.watchId} className="adm-attn" style={{ display: "block" }}>
              <span className="adm-mono adm-muted">{formatDateTime(row.startedAt)}</span>
              <span style={{ display: "block" }}>
                {row.playerName} · {row.videoTitle}
              </span>
              <span className="adm-sub">
                {formatWatchTime(row.watchSeconds)} · {formatPercent(row.completion)}
              </span>
            </div>
          ))}
          {sessions.nextCursor ? (
            <a className="adm-btn adm-btn-small" href={`/admin/viewers/${id}?cursor=${encodeURIComponent(sessions.nextCursor)}`} style={{ marginTop: 12 }}>
              {copy.loadMore}
            </a>
          ) : null}
        </section>
      </div>
    </>
  );
}
