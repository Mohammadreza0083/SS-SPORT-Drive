"use client";

import { useEffect, useRef, type KeyboardEvent, type MouseEvent } from "react";
import { EmailOverlay } from "@/components/viewer/player/email-overlay";
import { formatMatchDate, formatRate, matchResult, setScores, type ScoutReport } from "@/lib/viewer/scout";
import { scoutCopy } from "./copy";

/** Props of {@link ScoutReportView}. */
type ScoutReportViewProps = {
  /** The parsed scout file for this player. */
  report: ScoutReport;
  /** The page's player, named in the table's label. */
  playerName: string;
  /** The signed-in viewer's email, laid faintly over the stats (V-4), as on videos and PDFs. */
  viewerEmail?: string;
  /** Called when the viewer closes the dialog. */
  onClose: () => void;
};

/** What can take focus inside the dialog. */
const FOCUSABLE = "button, [href], [tabindex]:not([tabindex='-1'])";

/**
 * The view-only match stats dialog (backlog V-8, owner-approved 2026-11-12): the match header (teams, result, date, each
 * set's score) and this player's stats per skill. The scout file itself is never offered: the API parses it and only
 * these numbers reach the browser. It behaves like the document viewer: focus goes to Close, Escape closes, Tab stays
 * inside, the page behind does not scroll, there is no context menu, and the viewer's email lies over the content.
 *
 * @param props - See {@link ScoutReportViewProps}.
 * @returns The open dialog.
 */
export function ScoutReportView({ report, playerName, viewerEmail, onClose }: ScoutReportViewProps) {
  const dialogRef = useRef<HTMLElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const { match } = report;

  useEffect(() => {
    closeRef.current?.focus();
    const previous = window.document.body.style.overflow;
    window.document.body.style.overflow = "hidden";
    return () => {
      window.document.body.style.overflow = previous;
    };
  }, []);

  useEffect(() => {
    /**
     * Escape closes the dialog wherever focus is while it is open.
     *
     * @param event - The key press.
     */
    const onEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", onEscape);
    return () => window.removeEventListener("keydown", onEscape);
  }, [onClose]);

  /**
   * Keeps Tab inside the dialog.
   *
   * @param event - The key press.
   */
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key !== "Tab" || !dialogRef.current) {
      return;
    }
    const focusable = [...dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)];
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && window.document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && window.document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  };

  /**
   * Blocks the browser's context menu.
   *
   * @param event - The right click or long press.
   */
  const blockMenu = (event: MouseEvent) => event.preventDefault();

  const date = formatMatchDate(match.date);

  return (
    <div className="dv-root">
      <div className="dv-backdrop" aria-hidden="true" />
      <section
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={scoutCopy.dialogLabel(report.title)}
        className="dv sr"
        onKeyDown={onKeyDown}
        onContextMenu={blockMenu}
      >
        <header className="dv-head">
          <div className="dv-titles">
            <h2 className="dv-title">{report.title}</h2>
            <div className="dv-meta">
              <span className="dv-player">{playerName}</span>
              {date ? (
                <>
                  <span className="dv-dot" aria-hidden="true">
                    ·
                  </span>
                  <span>{date}</span>
                </>
              ) : null}
              <span className="dv-dot" aria-hidden="true">
                ·
              </span>
              <span className="dv-lock">{scoutCopy.viewOnly}</span>
            </div>
          </div>
          <button ref={closeRef} type="button" className="dv-close" aria-label={scoutCopy.close} onClick={onClose}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </header>

        <div className="sr-body">
          <div className="sr-match">
            <div className="sr-teams">
              <span className="sr-team">{match.homeTeam}</span>
              <span className="sr-vs" aria-hidden="true">
                vs
              </span>
              <span className="sr-team">{match.awayTeam}</span>
            </div>
            <div className="sr-result">{matchResult(report)}</div>
            <div className="sr-sets">{setScores(match)}</div>
            {report.player ? <div className="sr-player">{scoutCopy.playerLine(report.player.number, report.player.team)}</div> : null}
          </div>

          {report.player ? (
            <table className="sr-table" aria-label={scoutCopy.tableLabel(playerName)}>
              <thead>
                <tr>
                  <th scope="col">{scoutCopy.columns.skill}</th>
                  <th scope="col">{scoutCopy.columns.total}</th>
                  <th scope="col">{scoutCopy.columns.perfect}</th>
                  <th scope="col">{scoutCopy.columns.errors}</th>
                  <th scope="col">{scoutCopy.columns.success}</th>
                  <th scope="col">{scoutCopy.columns.efficiency}</th>
                </tr>
              </thead>
              <tbody>
                {report.skills.map((skill) => (
                  <tr key={skill.skill}>
                    <td className="sr-skill">{skill.skill}</td>
                    <td>{skill.total}</td>
                    <td>{skill.perfect}</td>
                    <td>{skill.error}</td>
                    <td>{formatRate(skill.successRate)}</td>
                    <td>{formatRate(skill.efficiency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p className="sr-missing">{scoutCopy.notInFile(playerName)}</p>
          )}

          {viewerEmail ? <EmailOverlay email={viewerEmail} /> : null}
        </div>

        <footer className="dv-foot">{scoutCopy.protectedShort}</footer>
      </section>
    </div>
  );
}
