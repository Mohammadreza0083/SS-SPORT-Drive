import type { Metadata } from "next";
import Link from "next/link";
import { adminCopy } from "@/components/admin/copy";
import { LinkActions } from "@/components/admin/links/link-actions";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatDate } from "@/lib/admin/people";
import { adminGet, firstParam, pageParam, requireAdmin } from "@/lib/admin/server";
import type { LinkRow, Paged } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.links.title };

/** The status filters of the chips. */
const FILTERS = ["active", "paused", "revoked"];

/** The columns of the table. */
const COLUMNS = "1.8fr 1.4fr 1fr 1fr 1fr 200px";

/**
 * `/admin/links`: every personal link (A-6 list, canvas `AdminLinks`): who it is for, its players, its code, when it
 * expires and its status, with Pause, Resume and Revoke. `?status=` filters; new links are made from a player's page.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function LinksPage({ searchParams }: PageProps<"/admin/links">) {
  const params = await searchParams;
  const requested = firstParam(params, "status");
  const status = FILTERS.includes(requested ?? "") ? (requested as string) : null;
  const page = pageParam(params);
  const { token } = await requireAdmin();
  const list = await adminGet<Paged<LinkRow>>(token, `/admin/links?page=${page}&pageSize=25${status ? `&status=${status}` : ""}`);
  const copy = adminCopy.links;
  const href = (filter: string | null, to: number) => {
    const query = new URLSearchParams();
    if (filter) {
      query.set("status", filter);
    }

    if (to > 1) {
      query.set("page", String(to));
    }

    const text = query.toString();
    return text ? `/admin/links?${text}` : "/admin/links";
  };
  return (
    <>
      <PageHeader
        title={copy.title}
        lead={copy.lead}
        actions={
          <Link className="adm-btn adm-btn-red" href="/admin/players">
            {copy.newLink}
          </Link>
        }
      />
      <div className="adm-chips" style={{ marginTop: 22 }}>
        {[["all", null], ...FILTERS.map((item) => [item, item] as const)].map(([key, value]) => (
          <a key={key} className="adm-chip" href={href(value, 1)} aria-current={status === value ? "true" : undefined}>
            {copy.filters[key as string]}
          </a>
        ))}
      </div>
      <section className="adm-table" aria-label={copy.title}>
        <div className="adm-scroll">
          <div style={{ minWidth: 900 }}>
            <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: COLUMNS }}>
              {copy.columns.map((column, index) => (
                <span key={index}>{column}</span>
              ))}
            </div>
            {list.items.map((link) => (
              <div key={link.id} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: COLUMNS }}>
                <span>
                  <span style={{ fontWeight: 600 }}>{link.recipientName}</span>
                  <span className="adm-sub">{link.recipientOrganisation}</span>
                </span>
                <span>{link.playerNames.slice(0, 2).join(", ")}{link.playerNames.length > 2 ? ` +${link.playerNames.length - 2}` : ""}</span>
                <span className="adm-mono" style={{ color: "var(--ss-red-soft)" }}>
                  /r/{link.code}
                </span>
                <span>{link.expiresAt ? formatDate(link.expiresAt) : "—"}</span>
                <span>
                  <StatusBadge status={link.access === "Open" ? "Active" : link.access} />
                </span>
                <LinkActions linkId={link.id} status={link.status} />
              </div>
            ))}
            {list.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
          </div>
        </div>
      </section>
      <nav className="adm-pager" aria-label={copy.title}>
        {page > 1 ? <a className="adm-btn adm-btn-small" href={href(status, page - 1)}>{adminCopy.players.previous}</a> : null}
        <span>{adminCopy.players.page(list.page, list.totalPages)}</span>
        {page < list.totalPages ? <a className="adm-btn adm-btn-small" href={href(status, page + 1)}>{adminCopy.players.next}</a> : null}
      </nav>
    </>
  );
}
