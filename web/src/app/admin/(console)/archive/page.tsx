import type { Metadata } from "next";
import { CsvUpload, ImportFolders, StartScanButton } from "@/components/admin/archive/archive-actions";
import { adminCopy } from "@/components/admin/copy";
import { PageHeader } from "@/components/admin/shell/page-header";
import { StatusBadge } from "@/components/admin/status-badge";
import { ARCHIVE_STATES, archiveHref, formatBytes, readArchiveQuery } from "@/lib/admin/archive";
import { formatRelative } from "@/lib/admin/format";
import { adminGet, requireAdmin } from "@/lib/admin/server";
import type { ArchiveFolderRow, ArchiveSummary, Paged } from "@/lib/admin/types";

export const metadata: Metadata = { title: adminCopy.archive.title };

/** The report's columns. */
const COLUMNS = "36px 2.4fr 1.6fr 1.4fr .8fr 1.2fr 1.1fr";

/**
 * `/admin/archive`: the archive migration (Epic 8, not on the canvas; built in the console's style). The newest scan and
 * its totals (M-1), the report's player folders with what their names suggest and how many files the R2 backup holds,
 * new folders imported as waiting players (M-2), and the CSV round trip for their details (M-3). `?state=` filters and
 * `?page=` pages the list.
 *
 * @param props - The page props with the search params.
 * @returns The page.
 */
export default async function ArchivePage({ searchParams }: PageProps<"/admin/archive">) {
  const query = readArchiveQuery(await searchParams);
  const { token } = await requireAdmin();
  const summary = await adminGet<ArchiveSummary>(token, "/admin/archive");
  const list = summary.report
    ? await adminGet<Paged<ArchiveFolderRow>>(token, `/admin/archive/folders?page=${query.page}&pageSize=50${query.state ? `&state=${query.state}` : ""}`)
    : null;
  const copy = adminCopy.archive;
  const now = new Date();
  const latest = summary.latestScan;
  const report = summary.report;
  const running = latest?.status === "Running";
  const tiles = report
    ? [
        { label: copy.tiles.folders, value: String(report.folders) },
        { label: copy.tiles.files, value: String(report.files) },
        { label: copy.tiles.size, value: formatBytes(report.bytes) },
        { label: copy.tiles.inBackup, value: String(report.inBackup) },
        { label: copy.tiles.missing, value: String(report.missingFromBackup) },
        { label: copy.tiles.skipped, value: String(report.skipped) },
      ]
    : [];
  const counts: Record<string, number> = { new: summary.new, linked: summary.linked, imported: summary.imported };

  return (
    <>
      <PageHeader title={copy.title} lead={copy.lead} actions={<StartScanButton running={running} />} />

      <section className="adm-card adm-archive-scan" aria-label={copy.status.Completed}>
        {latest ? (
          <>
            <StatusBadge status={latest.status === "Completed" ? "Ready" : latest.status} label={copy.status[latest.status] ?? latest.status} />
            <span className="adm-muted">
              {latest.finishedAt ? copy.finished(formatRelative(latest.finishedAt, now)) : copy.started(formatRelative(latest.startedAt, now))}
            </span>
            {latest.failureReason ? <span className="adm-hint-bad">{latest.failureReason}</span> : null}
          </>
        ) : (
          <span className="adm-muted">{copy.never}</span>
        )}
      </section>

      {tiles.length > 0 ? (
        <div className="adm-kpis">
          {tiles.map((tile) => (
            <div key={tile.label} className="adm-kpi">
              <div className="adm-label">{tile.label}</div>
              <div className="adm-kpi-value" style={{ fontSize: 28 }}>
                {tile.value}
              </div>
            </div>
          ))}
        </div>
      ) : null}

      {list ? (
        <>
          <div className="adm-chips">
            <a className="adm-chip" href={archiveHref({ state: null, page: 1 })} aria-current={query.state ? undefined : "true"}>
              {copy.states.all}
            </a>
            {ARCHIVE_STATES.map((state) => (
              <a key={state} className="adm-chip" href={archiveHref({ state, page: 1 })} aria-current={query.state === state ? "true" : undefined}>
                {`${copy.states[state]} · ${counts[state]}`}
              </a>
            ))}
          </div>
          <section className="adm-table" aria-label={copy.title}>
            <div className="adm-scroll">
              <div style={{ minWidth: 980 }}>
                <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: COLUMNS }}>
                  {copy.columns.map((column, index) => (
                    <span key={index}>{column}</span>
                  ))}
                </div>
                <ImportFolders
                  rows={list.items.map((row) => ({
                    id: row.id,
                    label: row.folderName,
                    selectable: row.state === "New",
                    children: <FolderCells row={row} />,
                  }))}
                />
                {list.items.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
              </div>
            </div>
          </section>
          {list.totalPages > 1 ? (
            <nav className="adm-chips" aria-label={copy.title}>
              {query.page > 1 ? (
                <a className="adm-chip" href={archiveHref({ ...query, page: query.page - 1 })}>
                  {copy.previous}
                </a>
              ) : null}
              <span className="adm-muted">{`${query.page} / ${list.totalPages}`}</span>
              {query.page < list.totalPages ? (
                <a className="adm-chip" href={archiveHref({ ...query, page: query.page + 1 })}>
                  {copy.next}
                </a>
              ) : null}
            </nav>
          ) : null}
        </>
      ) : null}

      <section className="adm-card adm-archive-csv-card" aria-label={copy.csvTitle}>
        <h2 className="adm-label">{copy.csvTitle}</h2>
        <p className="adm-muted">{copy.csvLead}</p>
        <div className="adm-archive-toolbar">
          <a className="adm-btn adm-btn-small" href="/api/admin/archive/waiting.csv" download>
            {copy.csvDownload}
          </a>
          <CsvUpload />
        </div>
      </section>
    </>
  );
}

/**
 * One report row's cells after the checkbox.
 *
 * @param props - The row.
 * @param props.row - The folder.
 * @returns The cells.
 */
function FolderCells({ row }: { row: ArchiveFolderRow }) {
  const copy = adminCopy.archive;
  return (
    <>
      <span>
        {row.folderName}
        <span className="adm-sub adm-muted">{row.category}</span>
      </span>
      <span>
        {row.suggestedName ?? <span className="adm-muted">{copy.noName}</span>}
        {row.suggestedPositions.length > 0 ? <span className="adm-sub adm-mono">{row.suggestedPositions.join(" / ")}</span> : null}
      </span>
      <span>
        {copy.files(row.videos, row.documents, row.scouts)}
        {row.isTruncated ? <span className="adm-sub adm-hint-bad">{copy.truncated}</span> : null}
      </span>
      <span className="adm-mono">{formatBytes(row.bytes)}</span>
      <span className={row.missingFromBackup > 0 ? "adm-hint-bad" : undefined}>{copy.backup(row.inBackup, row.missingFromBackup)}</span>
      <span>
        {row.playerId ? (
          <a href={`/admin/players/${row.playerId}`}>{copy.states[row.state.toLowerCase()] ?? row.state}</a>
        ) : (
          <StatusBadge status="Pending" label={copy.states.new} />
        )}
      </span>
    </>
  );
}
