"use client";

import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { checkCsvText } from "@/lib/admin/archive";
import { AdminActionError, adminCall } from "@/lib/admin/client";
import { adminCopy } from "../copy";

/** The archive page's words. */
const copy = adminCopy.archive;

/** Props of {@link StartScanButton}. */
type StartScanButtonProps = {
  /** Whether a scan is running, so the button waits. */
  running: boolean;
};

/**
 * Starts a read-only scan of the Shared Drive (M-1), then refreshes the page to show it running.
 *
 * @param props - See {@link StartScanButtonProps}.
 * @returns The button and any error.
 */
export function StartScanButton({ running }: StartScanButtonProps) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /** Sends the start. */
  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await adminCall("POST", "archive/scans", {});
      router.refresh();
    } catch (failure) {
      setError(failure instanceof AdminActionError && failure.code === "archive.scan_running" ? copy.scanRunning : copy.failed);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="adm-archive-action">
      <button type="button" className="adm-btn adm-btn-red" disabled={running || busy} onClick={() => void start()}>
        {running || busy ? copy.scanning : copy.scan}
      </button>
      {error ? (
        <p role="alert" className="adm-hint-bad">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** One importable folder. */
type ImportRow = {
  /** The report row id. */
  id: string;
  /** What the checkbox is called (the folder name). */
  label: string;
  /** What else the row shows next to the checkbox. */
  children?: ReactNode;
  /** Whether the folder can be imported (only new ones can); others show without a checkbox. Defaults to true. */
  selectable?: boolean;
};

/** Props of {@link ImportFolders}. */
type ImportFoldersProps = {
  /** The page's folders, in list order. */
  rows: ImportRow[];
};

/**
 * Picks new folders on the page and imports them as waiting archive players (M-2), then refreshes the list.
 *
 * @param props - See {@link ImportFoldersProps}.
 * @returns The checkboxes and the Import button.
 */
export function ImportFolders({ rows }: ImportFoldersProps) {
  const router = useRouter();
  const [chosen, setChosen] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const selectable = rows.filter((row) => row.selectable !== false);
  const allChosen = selectable.length > 0 && chosen.length === selectable.length;

  /**
   * Adds or removes one folder.
   *
   * @param id - The row id.
   */
  const toggle = (id: string) => setChosen((current) => (current.includes(id) ? current.filter((item) => item !== id) : [...current, id]));

  /** Sends the import. */
  const importChosen = async () => {
    setBusy(true);
    setError(null);
    try {
      const answer = await adminCall<{ imported: unknown[]; refused: unknown[] }>("POST", "archive/import", { folderIds: chosen });
      setResult(copy.imported(answer.imported.length, answer.refused.length));
      setChosen([]);
      router.refresh();
    } catch {
      setError(copy.failed);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="adm-archive-import">
      <div className="adm-archive-toolbar">
        <label className="adm-pick">
          <input type="checkbox" checked={allChosen} disabled={selectable.length === 0} onChange={() => setChosen(allChosen ? [] : selectable.map((row) => row.id))} />
          {copy.selectAll}
        </label>
        <button type="button" className="adm-btn adm-btn-red adm-btn-small" disabled={busy || chosen.length === 0} onClick={() => void importChosen()}>
          {busy ? copy.importing : copy.import(chosen.length)}
        </button>
      </div>
      {result ? (
        <p role="status" className="adm-muted">
          {result}
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="adm-hint-bad">
          {error}
        </p>
      ) : null}
      {rows.map((row) => (
        <div key={row.id} className="adm-grid adm-grid-row adm-archive-row">
          {row.selectable === false ? (
            <span className="adm-pick" aria-hidden="true" />
          ) : (
            <label className="adm-pick">
              <input type="checkbox" checked={chosen.includes(row.id)} onChange={() => toggle(row.id)} aria-label={row.label} />
            </label>
          )}
          {row.children}
        </div>
      ))}
    </div>
  );
}

/** What the CSV import answered. */
type CsvResult = { updated: number; unchanged: number; errors: { row: number; code: string; message: string }[] };

/**
 * Sends the filled-in CSV back (M-3): the file is read in the browser and sent as JSON text, then the result and the
 * refused rows are listed.
 *
 * @returns The file picker and the result.
 */
export function CsvUpload() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CsvResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  /**
   * Reads and sends the chosen file.
   *
   * @param file - The file, if one was chosen.
   */
  const send = async (file: File | undefined) => {
    if (!file) {
      return;
    }

    setResult(null);
    setError(null);
    const text = await file.text();
    const problem = checkCsvText(text);
    if (problem) {
      setError(problem === "empty" ? copy.csvEmpty : copy.csvTooLarge);
      return;
    }

    setBusy(true);
    try {
      setResult(await adminCall<CsvResult>("POST", "archive/waiting.csv", { csv: text }));
      router.refresh();
    } catch {
      setError(copy.failed);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="adm-archive-csv">
      <label className="adm-btn adm-btn-small adm-file">
        {busy ? copy.csvUploading : copy.csvUpload}
        <input type="file" accept=".csv,text/csv" aria-label={copy.csvUpload} disabled={busy} onChange={(event) => void send(event.target.files?.[0])} />
      </label>
      {error ? (
        <p role="alert" className="adm-hint-bad">
          {error}
        </p>
      ) : null}
      {result ? (
        <>
          <p role="status" className="adm-muted">
            {copy.csvResult(result.updated, result.unchanged, result.errors.length)}
          </p>
          {result.errors.length > 0 ? (
            <ul className="adm-archive-errors">
              {result.errors.map((item) => (
                <li key={item.row}>{copy.csvError(item.row, item.message, item.code)}</li>
              ))}
            </ul>
          ) : null}
        </>
      ) : null}
    </div>
  );
}
