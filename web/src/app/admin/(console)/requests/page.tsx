import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { RequestDecision } from "@/components/admin/requests/request-decision";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatRelative } from "@/lib/admin/format";
import { readRequestsQuery, REQUEST_STATUSES, requestsHref } from "@/lib/admin/requests";
import { adminGet, requireAdmin } from "@/lib/admin/server";
import type { AccessRequestRow, Paged, SettingsData } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.requests.title };

/** Requests on one page of the list. */
const PAGE_SIZE = 20;

/**
 * `/admin/requests`: the access-request inbox (A-12, canvas `AdminRequests`, on P-4's API). Tabs Pending, Approved and
 * Declined with their counts (`?status=`), one page of requests newest first (`?page=`, paged in SQL by the API), and the
 * open request (`?id=`, else the first) with the approval form. Approving makes a personal link and emails it; declining
 * emails nobody.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function RequestsPage({ searchParams }: PageProps<"/admin/requests">) {
  const query = readRequestsQuery(await searchParams);
  const { token } = await requireAdmin();
  const [list, settings, ...counts] = await Promise.all([
    adminGet<Paged<AccessRequestRow>>(token, `/admin/access-requests?status=${query.status}&page=${query.page}&pageSize=${PAGE_SIZE}`),
    adminGet<SettingsData>(token, "/admin/settings"),
    ...REQUEST_STATUSES.map((status) => adminGet<Paged<AccessRequestRow>>(token, `/admin/access-requests?status=${status}&pageSize=1`)),
  ]);
  const copy = adminCopy.requests;
  const now = new Date();
  const open = list.items.find((request) => request.id === query.id) ?? list.items[0] ?? null;
  return (
    <>
      <PageHeader
        title={copy.title}
        lead={copy.lead}
        actions={
          <div className="adm-chips">
            {REQUEST_STATUSES.map((status, index) => (
              <a key={status} className="adm-chip" href={requestsHref({ status, page: 1, id: null })} aria-current={query.status === status ? "true" : undefined}>
                {copy.tab(copy.tabs[status], counts[index].totalCount)}
              </a>
            ))}
          </div>
        }
      />
      <div className="adm-req-layout">
        <section className="adm-card adm-req-list" aria-label={copy.list}>
          {list.items.map((request) => (
            <a
              key={request.id}
              className="adm-req-row"
              href={requestsHref({ ...query, id: request.id })}
              aria-current={open?.id === request.id ? "true" : undefined}
            >
              <span className="adm-req-row-top">
                <span className="adm-req-row-email">{request.email}</span>
                <span className="adm-mono adm-muted" style={{ fontSize: 11, flex: "none" }}>
                  {formatRelative(request.createdAt, now)}
                </span>
              </span>
              <span className="adm-name" style={{ display: "block", fontSize: 16, marginTop: 6 }}>
                {request.player.name ?? "—"}
              </span>
              <span className="adm-inline" style={{ marginTop: 8 }}>
                <StatusBadge status={request.status} />
                <StatusBadge status="Hidden" label={copy.verified} />
              </span>
            </a>
          ))}
          {list.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
          {list.totalPages > 1 ? (
            <nav className="adm-pager" aria-label={copy.list} style={{ padding: "12px 18px" }}>
              {query.page > 1 ? (
                <a className="adm-btn adm-btn-small" href={requestsHref({ ...query, page: query.page - 1, id: null })}>
                  {copy.previous}
                </a>
              ) : null}
              <span>{adminCopy.players.page(list.page, list.totalPages)}</span>
              {query.page < list.totalPages ? (
                <a className="adm-btn adm-btn-small" href={requestsHref({ ...query, page: query.page + 1, id: null })}>
                  {copy.next}
                </a>
              ) : null}
            </nav>
          ) : null}
        </section>
        {open ? (
          <RequestDecision
            key={open.id}
            request={open}
            defaultExpiryDays={settings.defaultLinkExpiryDays}
            nextHref={requestsHref({ status: "pending", page: 1, id: null })}
          />
        ) : null}
      </div>
    </>
  );
}
