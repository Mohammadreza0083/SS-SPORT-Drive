import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatRelative } from "@/lib/admin/format";
import { PLAYER_POSITIONS, PLAYER_STATUSES, parsePlayersQuery, playersApiPath, playersCsvHref, playersHref, withFilter } from "@/lib/admin/players-query";
import { adminGet, requireAdmin } from "@/lib/admin/server";
import type { Paged, PlayerRow, WaitingSummary } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.players.title };

/** The columns of the table. */
const COLUMNS = "2.2fr .8fr 1fr 1fr .9fr 1.1fr 1fr 1fr";

/**
 * `/admin/players`: every player (A-3, canvas `AdminPlayers`). The search, the position and status filters and the page
 * live in the address (`?q=`, `?position=`, `?status=`, `?page=`), so a view can be bookmarked and shared; the list is paged
 * in SQL, and Export CSV downloads the same filters.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function PlayersPage({ searchParams }: PageProps<"/admin/players">) {
  const query = parsePlayersQuery(await searchParams);
  const { token } = await requireAdmin();
  const [list, waiting] = await Promise.all([adminGet<Paged<PlayerRow>>(token, playersApiPath(query)), adminGet<WaitingSummary>(token, "/admin/waiting/summary")]);
  const copy = adminCopy.players;
  const now = new Date();
  return (
    <>
      <PageHeader
        title={copy.title}
        lead={copy.lead}
        actions={
          <>
            <a className="adm-btn" href={playersCsvHref(query)} download>
              {copy.exportCsv}
            </a>
            {waiting.waiting > 0 ? (
              <a className="adm-btn adm-btn-red" href="/admin/waiting">
                {copy.waiting(waiting.waiting)}
              </a>
            ) : null}
          </>
        }
      />
      <form method="get" action="/admin/players" className="adm-search" role="search">
        <label htmlFor="p-q" className="adm-sr">
          {copy.search}
        </label>
        <input id="p-q" name="q" type="search" className="adm-input" defaultValue={query.q} placeholder={copy.searchPlaceholder} maxLength={100} />
        {query.position ? <input type="hidden" name="position" value={query.position} /> : null}
        {query.status ? <input type="hidden" name="status" value={query.status} /> : null}
        <button type="submit" className="adm-btn">
          {copy.searchButton}
        </button>
      </form>
      <div className="adm-chips">
        <a className="adm-chip" href={playersHref(withFilter(query, { position: null }))} aria-current={query.position === null ? "true" : undefined}>
          {copy.all}
        </a>
        {PLAYER_POSITIONS.map((code) => (
          <a key={code} className="adm-chip" href={playersHref(withFilter(query, { position: code }))} aria-current={query.position === code ? "true" : undefined}>
            {code}
          </a>
        ))}
      </div>
      <div className="adm-chips">
        <a className="adm-chip" href={playersHref(withFilter(query, { status: null }))} aria-current={query.status === null ? "true" : undefined}>
          {copy.all}
        </a>
        {PLAYER_STATUSES.map((status) => (
          <a key={status} className="adm-chip" href={playersHref(withFilter(query, { status }))} aria-current={query.status === status ? "true" : undefined}>
            {copy.statuses[status]}
          </a>
        ))}
      </div>
      <section className="adm-table" aria-label={copy.title}>
        <div className="adm-scroll">
          <div style={{ minWidth: 860 }}>
            <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: COLUMNS }}>
              {copy.columns.map((column) => (
                <span key={column}>{column}</span>
              ))}
            </div>
            {list.items.map((row) => (
              <a key={row.id} href={`/admin/players/${row.id}`} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: COLUMNS }}>
                <span className="adm-inline">
                  <span className="adm-face" />
                  <span className="adm-name">{row.name ?? copy.unnamed}</span>
                </span>
                <span className="adm-mono">{row.positions.join(" ")}</span>
                <span>{row.nationalityName ?? "—"}</span>
                <span className="adm-mono">{copy.files(row.videoCount, row.documentCount)}</span>
                <span className="adm-mono">{row.viewers}</span>
                <span className="adm-muted">{formatRelative(row.lastViewedAt, now)}</span>
                <span>{row.analystName ?? "—"}</span>
                <span>
                  <StatusBadge status={row.status} />
                </span>
              </a>
            ))}
            {list.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
          </div>
        </div>
      </section>
      <nav className="adm-pager" aria-label={copy.title}>
        {query.page > 1 ? <a className="adm-btn adm-btn-small" href={playersHref({ ...query, page: query.page - 1 })}>{copy.previous}</a> : null}
        <span>{copy.page(list.page, list.totalPages)}</span>
        {query.page < list.totalPages ? <a className="adm-btn adm-btn-small" href={playersHref({ ...query, page: query.page + 1 })}>{copy.next}</a> : null}
      </nav>
    </>
  );
}
