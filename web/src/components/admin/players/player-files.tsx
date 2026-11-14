"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { adminCall } from "@/lib/admin/client";
import { formatWatchTime } from "@/lib/admin/format";
import type { PlayerFile } from "@/lib/admin/types";
import { StatusBadge } from "../status-badge";
import { adminCopy } from "../copy";

/** Props of {@link PlayerFiles}. */
type PlayerFilesProps = {
  /** The player. */
  playerId: string;
  /** The player's files in page order. */
  files: PlayerFile[];
};

/**
 * The files tab of the player page (A-4, canvas `AdminPlayer`): the files in page order with Move up and Move down, the
 * hero star, the visibility switch and Retry for failed files. Each action calls the API through the console's route and
 * refreshes the page.
 *
 * @param props - See {@link PlayerFilesProps}.
 * @returns The files table.
 */
export function PlayerFiles({ playerId, files }: PlayerFilesProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const copy = adminCopy.player;

  /**
   * Runs one action, then refreshes the list.
   *
   * @param action - The API call.
   */
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setError(false);
    try {
      await action();
      router.refresh();
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  };

  if (files.length === 0) {
    return <div className="adm-empty">{copy.noFiles}</div>;
  }

  return (
    <div className="adm-scroll">
      <div style={{ minWidth: 820 }}>
        <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "84px 2.4fr 1fr .8fr .8fr 1fr 80px" }}>
          {copy.filesColumns.map((column, index) => (
            <span key={index}>{column}</span>
          ))}
        </div>
        {files.map((file, index) => (
          <div key={file.id} className="adm-grid adm-grid-row" style={{ gridTemplateColumns: "84px 2.4fr 1fr .8fr .8fr 1fr 80px", opacity: file.visibility === "Hidden" ? 0.55 : 1 }}>
            <span className="adm-inline">
              <button type="button" className="adm-btn adm-btn-small" aria-label={`${copy.moveUp}: ${file.title}`} disabled={busy || index === 0} onClick={() => void run(() => adminCall("POST", `players/${playerId}/files/${file.id}/move`, { position: index }))}>
                ↑
              </button>
              <button type="button" className="adm-btn adm-btn-small" aria-label={`${copy.moveDown}: ${file.title}`} disabled={busy || index === files.length - 1} onClick={() => void run(() => adminCall("POST", `players/${playerId}/files/${file.id}/move`, { position: index + 2 }))}>
                ↓
              </button>
            </span>
            <span>
              {file.title}
              {file.isHero ? <span className="adm-pill adm-pill-red" style={{ marginLeft: 8 }}>{copy.hero}</span> : null}
              {file.kind === "Video" && !file.isHero && file.state === "Ready" && file.visibility === "Visible" ? (
                <button type="button" className="adm-btn adm-btn-small" style={{ marginLeft: 8 }} disabled={busy} onClick={() => void run(() => adminCall("PATCH", `players/${playerId}/files/${file.id}`, { isHero: true }))}>
                  {copy.makeHero}
                </button>
              ) : null}
              <span className="adm-sub">{file.removedFromSource ? copy.removed : file.fileName}</span>
              {file.failureReason ? <span className="adm-sub adm-hint-bad">{file.failureReason}</span> : null}
            </span>
            <span>{file.tags.join(", ") || "—"}</span>
            <span className="adm-mono">{file.durationSeconds ? formatWatchTime(file.durationSeconds) : file.pageCount ? `${file.pageCount} p` : "—"}</span>
            <span>{file.source}</span>
            <span className="adm-inline">
              <StatusBadge status={file.state} />
              {file.state === "Failed" ? (
                <button type="button" className="adm-btn adm-btn-small" disabled={busy} onClick={() => void run(() => adminCall("POST", `files/${file.id}/retry`, {}))}>
                  {copy.retry}
                </button>
              ) : null}
            </span>
            <button type="button" role="switch" className="adm-switch" aria-checked={file.visibility === "Visible"} aria-label={`${copy.visible}: ${file.title}`} disabled={busy} onClick={() => void run(() => adminCall("PATCH", `players/${playerId}/files/${file.id}`, { hidden: file.visibility === "Visible" }))} />
          </div>
        ))}
        <div className="adm-grid adm-muted">{copy.pdfNote}</div>
        {error ? (
          <p role="alert" className="adm-error" style={{ padding: "0 20px 14px" }}>
            {copy.failed}
          </p>
        ) : null}
      </div>
    </div>
  );
}
