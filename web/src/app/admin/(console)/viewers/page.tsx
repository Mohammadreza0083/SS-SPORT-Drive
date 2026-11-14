import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatRelative, formatWatchTime } from "@/lib/admin/format";
import { formatDate } from "@/lib/admin/people";
import { adminGet, firstParam, pageParam, requireAdmin } from "@/lib/admin/server";
import type { Paged, ViewerRow } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.viewers.title };

/** The activity filters of the chips. */
const FILTERS = ["active", "never"];

/** The columns of the table. */
const COLUMNS = "2.2fr 1.6fr .7fr .8fr 1fr 1fr 1fr";

/**
 * `/admin/viewers`: everyone who can watch (A-11, canvas `AdminViewers`): the players they can open, plays, time
 * watched, last seen, when their access ends and how they sign in. `?q=`, `?activity=` and `?page=` live in the address.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function ViewersPage({ searchParams }: PageProps<"/admin/viewers">) {
  const params = await searchParams;
  const q = (firstParam(params, "q") ?? "").trim().slice(0, 100);
  const requested = firstParam(params, "activity");
  const activity = FILTERS.includes(requested ?? "") ? (requested as string) : null;
  const page = pageParam(params);
  const { token } = await requireAdmin();
  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (q) {
    query.set("q", q);
  }

  if (activity) {
    query.set("activity", activity);
  }

  const list = await adminGet<Paged<ViewerRow>>(token, `/admin/viewers?${query.toString()}`);
  const copy = adminCopy.viewers;
  const now = new Date();
  const href = (filter: string | null, to: number) => {
    const next = new URLSearchParams();
    if (q) {
      next.set("q", q);
    }

    if (filter) {
      next.set("activity", filter);
    }

    if (to > 1) {
      next.set("page", String(to));
    }

    const text = next.toString();
    return text ? `/admin/viewers?${text}` : "/admin/viewers";
  };
  return (
    <>
      <PageHeader title={copy.title} lead={copy.lead} />
      <form method="get" action="/admin/viewers" className="adm-search" role="search">
        <label htmlFor="v-q" className="adm-sr">
          {copy.search}
        </label>
        <input id="v-q" name="q" type="search" className="adm-input" defaultValue={q} placeholder={copy.searchPlaceholder} maxLength={100} />
        {activity ? <input type="hidden" name="activity" value={activity} /> : null}
        <button type="submit" className="adm-btn">
          {adminCopy.players.searchButton}
        </button>
        {[["all", null], ["active", "active"], ["never", "never"]].map(([key, value]) => (
          <a key={key} className="adm-chip" href={href(value, 1)} aria-current={activity === value ? "true" : undefined}>
            {copy.filters[key as string]}
          </a>
        ))}
      </form>
      <section className="adm-table" aria-label={copy.title}>
        <div className="adm-scroll">
          <div style={{ minWidth: 900 }}>
            <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: COLUMNS }}>
              {copy.columns.map((column) => (
                <span key={column}>{column}</span>
              ))}
            </div>
            {list.items.map((viewer) => (
              <a key={viewer.id} href={`/admin/viewers/${viewer.id}`} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: COLUMNS }}>
                <span>
                  <span style={{ display: "block", overflow: "hidden", textOverflow: "ellipsis" }}>{viewer.email}</span>
                  <span className="adm-sub">{viewer.organisation}</span>
                </span>
                <span>
                  {viewer.players.join(", ")}
                  {viewer.playerCount > viewer.players.length ? ` +${viewer.playerCount - viewer.players.length}` : ""}
                </span>
                <span className="adm-mono">{viewer.plays}</span>
                <span className="adm-mono">{formatWatchTime(viewer.watchSeconds)}</span>
                <span className="adm-muted">{viewer.lastSeenAt ? formatRelative(viewer.lastSeenAt, now) : copy.never}</span>
                <span>{formatDate(viewer.accessUntil)}</span>
                <span>
                  <StatusBadge status={viewer.account} label={copy.accounts[viewer.account]} />
                </span>
              </a>
            ))}
            {list.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
          </div>
        </div>
      </section>
      <nav className="adm-pager" aria-label={copy.title}>
        {page > 1 ? <a className="adm-btn adm-btn-small" href={href(activity, page - 1)}>{adminCopy.players.previous}</a> : null}
        <span>{adminCopy.players.page(list.page, list.totalPages)}</span>
        {page < list.totalPages ? <a className="adm-btn adm-btn-small" href={href(activity, page + 1)}>{adminCopy.players.next}</a> : null}
      </nav>
    </>
  );
}
