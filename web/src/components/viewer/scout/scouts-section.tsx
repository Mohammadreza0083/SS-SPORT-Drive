"use client";

import { useId, useRef, useState } from "react";
import { MediaRequestError, fetchScoutReport } from "@/lib/viewer/media-client";
import type { PlayerScout, ScoutReport } from "@/lib/viewer/scout";
import { scoutCopy } from "./copy";
import { ScoutReportView } from "./scout-report";

/** Props of {@link ScoutsSection}. */
type ScoutsSectionProps = {
  /** The player's DataVolley files. */
  scouts: PlayerScout[];
  /** The player's name, shown in the dialog. */
  playerName: string;
  /** The signed-in viewer's email, laid over the stats (V-4). */
  viewerEmail?: string;
};

/**
 * The Match stats section (backlog V-8): one card per DataVolley file the player has. Opening one reads its report
 * from the API (parsed on the server, never the file) and shows it in a view-only dialog. When the file cannot be
 * read, a short message says so. Nothing is drawn when the player has no scout files. Closing the dialog brings focus
 * back to the card that opened it.
 *
 * @param props - See {@link ScoutsSectionProps}.
 * @returns The section, with the dialog while a report is open, or nothing.
 */
export function ScoutsSection({ scouts, playerName, viewerEmail }: ScoutsSectionProps) {
  const headingId = useId();
  const [report, setReport] = useState<ScoutReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cardRefs = useRef(new Map<string, HTMLButtonElement>());

  if (scouts.length === 0) {
    return null;
  }

  /**
   * Reads and opens one scout file's report.
   *
   * @param scout - The file whose card was pressed.
   */
  const openReport = (scout: PlayerScout) => {
    setError(null);
    fetchScoutReport(scout.reportPath).then(setReport, (failure: unknown) =>
      setError(failure instanceof MediaRequestError && failure.status === 422 ? scoutCopy.unreadable : scoutCopy.unavailable),
    );
  };

  /** Closes the dialog and returns focus to the card that opened it. */
  const close = () => {
    const card = report ? cardRefs.current.get(report.id) : undefined;
    setReport(null);
    card?.focus();
  };

  return (
    <section id="matches" className="pp-docs sr-section" aria-labelledby={headingId}>
      <h2 id={headingId} className="ss-h2">
        {scoutCopy.section}
      </h2>
      <div className="pp-doc-grid">
        {scouts.map((scout) => (
          <button
            key={scout.id}
            type="button"
            ref={(element) => {
              if (element) {
                cardRefs.current.set(scout.id, element);
              } else {
                cardRefs.current.delete(scout.id);
              }
            }}
            className="pp-doc ss-doc ss-press ss-sr"
            onClick={() => openReport(scout)}
          >
            <span className="pp-doc-text">
              <span className="pp-doc-title">{scout.title}</span>
              <span className="pp-doc-open">{scoutCopy.viewOnly} →</span>
            </span>
            <span className="pp-doc-view" aria-hidden="true">
              {scoutCopy.open}
            </span>
          </button>
        ))}
      </div>
      {error ? (
        <p className="pp-unavailable" role="alert">
          {error}
        </p>
      ) : null}
      {report ? <ScoutReportView report={report} playerName={playerName} viewerEmail={viewerEmail} onClose={close} /> : null}
    </section>
  );
}
