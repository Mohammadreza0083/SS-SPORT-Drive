import type { Metadata } from "next";
import { adminCopy } from "@/components/admin/copy";
import { RetryButton } from "@/components/admin/jobs/retry-button";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { formatRelative } from "@/lib/admin/format";
import { adminGet, firstParam, pageParam, requireAdmin } from "@/lib/admin/server";
import type { Cursored, EmailRow, JobRow, JobsSummary, Paged } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.jobs.title };

/** The state filters the jobs list accepts. */
const STATES = ["queued", "copying", "processing", "failed"];

/**
 * `/admin/jobs`: the files being ingested and the email queue (A-9, canvas `AdminQueues`). `?tab=email` shows the queue
 * (`?cursor=` pages it); on the Jobs tab `?state=` filters. Failed files and emails have a Retry button.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function JobsPage({ searchParams }: PageProps<"/admin/jobs">) {
  const params = await searchParams;
  const email = firstParam(params, "tab") === "email";
  const requested = firstParam(params, "state");
  const state = STATES.includes(requested ?? "") ? (requested as string) : null;
  const cursor = firstParam(params, "cursor");
  const { token } = await requireAdmin();
  const summary = await adminGet<JobsSummary>(token, "/admin/jobs/summary");
  const jobs = email ? null : await adminGet<Paged<JobRow>>(token, `/admin/jobs?page=${pageParam(params)}&pageSize=25${state ? `&state=${state}` : ""}`);
  const emails = email ? await adminGet<Cursored<EmailRow>>(token, `/admin/emails?pageSize=20${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`) : null;
  const copy = adminCopy.jobs;
  const now = new Date();
  const tiles = email
    ? [
        { label: copy.tiles.sent, value: String(summary.emailsSentLast24Hours) },
        { label: copy.tiles.mailQueued, value: String(summary.emailsQueued) },
        { label: copy.tiles.mailFailed, value: String(summary.emailsFailed) },
      ]
    : [
        { label: copy.tiles.queued, value: String(summary.queued + summary.copying) },
        { label: copy.tiles.processing, value: String(summary.processing) },
        { label: copy.tiles.failed, value: String(summary.failed) },
        { label: copy.tiles.sync, value: formatRelative(summary.lastDriveSyncAt, now) },
      ];
  return (
    <>
      <PageHeader
        title={copy.title}
        lead={copy.lead}
        actions={
          <div className="adm-chips">
            <a className="adm-chip" href="/admin/jobs" aria-current={email ? undefined : "true"}>
              {copy.tabs.jobs}
            </a>
            <a className="adm-chip" href="/admin/jobs?tab=email" aria-current={email ? "true" : undefined}>
              {copy.tabs.email}
            </a>
          </div>
        }
      />
      <div className="adm-kpis">
        {tiles.map((tile) => (
          <div key={tile.label} className="adm-kpi">
            <div className="adm-label">{tile.label}</div>
            <div className="adm-kpi-value" style={{ fontSize: 28 }}>
              {tile.value}
            </div>
            {tile.label === copy.tiles.sync ? <div className="adm-kpi-delta adm-muted">{copy.folders(summary.folders, summary.lostFolders)}</div> : null}
          </div>
        ))}
      </div>

      {jobs ? (
        <>
          <div className="adm-chips">
            <a className="adm-chip" href="/admin/jobs" aria-current={state ? undefined : "true"}>
              {adminCopy.players.all}
            </a>
            {STATES.map((item) => (
              <a key={item} className="adm-chip" href={`/admin/jobs?state=${item}`} aria-current={state === item ? "true" : undefined}>
                {item[0].toUpperCase() + item.slice(1)}
              </a>
            ))}
          </div>
          <section className="adm-table" aria-label={copy.tabs.jobs}>
            <div className="adm-scroll">
              <div style={{ minWidth: 820 }}>
                <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "1fr 1.4fr 2fr 1.2fr 100px" }}>
                  {copy.columns.map((column, index) => (
                    <span key={index}>{column}</span>
                  ))}
                </div>
                {jobs.items.map((job) => (
                  <div key={job.assetId} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "1fr 1.4fr 2fr 1.2fr 100px" }}>
                    <span>{job.kind}</span>
                    <a href={`/admin/players/${job.playerId}`}>{job.playerName}</a>
                    <span>
                      {job.fileName}
                      {job.failureReason ? <span className="adm-sub adm-hint-bad">{job.failureReason}</span> : null}
                    </span>
                    <span>
                      <StatusBadge status={job.state} />
                    </span>
                    <span>{job.state === "Failed" ? <RetryButton path={`files/${job.assetId}/retry`} /> : null}</span>
                  </div>
                ))}
                {jobs.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
              </div>
            </div>
          </section>
        </>
      ) : null}

      {emails ? (
        <>
          <section className="adm-table" aria-label={copy.tabs.email}>
            <div className="adm-scroll">
              <div style={{ minWidth: 900 }}>
                <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "2fr 1.2fr 1fr .5fr 1fr 1fr 90px" }}>
                  {copy.emailColumns.map((column, index) => (
                    <span key={index}>{column}</span>
                  ))}
                </div>
                {emails.items.map((row) => (
                  <div key={row.id} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "2fr 1.2fr 1fr .5fr 1fr 1fr 90px" }}>
                    <span>{row.recipient}</span>
                    <span>{row.template}</span>
                    <span>{row.provider ?? "—"}</span>
                    <span className="adm-mono">{row.attempts}</span>
                    <span>
                      <StatusBadge status={row.status === "Queued" && row.attempts > 0 ? "Pending" : row.status} label={row.status === "Queued" && row.attempts > 0 ? "Retrying" : row.status} />
                    </span>
                    <span className="adm-muted">{formatRelative(row.queuedAt, now)}</span>
                    <span>{row.status === "Failed" ? <RetryButton path={`emails/${row.id}/retry`} /> : null}</span>
                  </div>
                ))}
                {emails.items.length === 0 ? <div className="adm-empty">{copy.emailEmpty}</div> : null}
              </div>
            </div>
          </section>
          {emails.nextCursor ? (
            <a className="adm-btn adm-btn-small" href={`/admin/jobs?tab=email&cursor=${encodeURIComponent(emails.nextCursor)}`} style={{ marginTop: 12 }}>
              {copy.loadMore}
            </a>
          ) : null}
          <p className="adm-hint">{copy.privacy}</p>
        </>
      ) : null}
    </>
  );
}
