"use client";

import { useRef, useState } from "react";
import { DocumentViewer } from "@/components/viewer/document-viewer/document-viewer";
import { fetchPages } from "@/lib/viewer/media-client";
import { pageCopy } from "./copy";
import type { PlayerDocument } from "./types";

/** Props of {@link DocumentsSection}. */
type DocumentsSectionProps = {
  /** The visible documents. */
  documents: PlayerDocument[];
  /** The player's name, shown in the viewer's header. */
  playerName: string;
  /** The signed-in viewer's email, laid over every page. */
  viewerEmail?: string;
};

/**
 * The Documents section (V-2 layout) whose cards open the view-only document viewer (V-6) in the page. Closing
 * the viewer brings focus back to the card that opened it.
 *
 * @param props - See {@link DocumentsSectionProps}.
 * @returns The section, with the viewer while a document is open.
 */
export function DocumentsSection({ documents, playerName, viewerEmail }: DocumentsSectionProps) {
  const [openId, setOpenId] = useState<string | null>(null);
  const [signedPages, setSignedPages] = useState<string[] | null>(null);
  const [failed, setFailed] = useState(false);
  const cardRefs = useRef(new Map<string, HTMLButtonElement>());
  const open = documents.find((doc) => doc.id === openId);

  /**
   * Opens a document. One with a pages route (V-5b) has its page images signed first, each time it is opened, because the
   * token lives 30 minutes; a document with fixed pages (the design preview) opens at once.
   *
   * @param doc - The document whose card was pressed.
   */
  const openDocument = (doc: PlayerDocument) => {
    setFailed(false);
    if (doc.pages.length > 0 || !doc.pagesPath) {
      setSignedPages(null);
      setOpenId(doc.id);
      return;
    }
    fetchPages(doc.pagesPath).then(
      (pages) => {
        if (pages.length === 0) {
          setFailed(true);
          return;
        }
        setSignedPages(pages);
        setOpenId(doc.id);
      },
      () => setFailed(true),
    );
  };

  /** Closes the viewer and returns focus to the card that opened it. */
  const close = () => {
    const card = openId ? cardRefs.current.get(openId) : undefined;
    setOpenId(null);
    setSignedPages(null);
    card?.focus();
  };

  return (
    <section id="docs" className="pp-docs" aria-labelledby="pp-docs-title">
      <h2 id="pp-docs-title" className="ss-h2 ss-sr">
        {pageCopy.documents}
      </h2>
      <div className="pp-doc-grid">
        {documents.map((doc, i) => (
          <button
            key={doc.id}
            type="button"
            ref={(element) => {
              if (element) {
                cardRefs.current.set(doc.id, element);
              } else {
                cardRefs.current.delete(doc.id);
              }
            }}
            className="pp-doc ss-doc ss-press ss-sr"
            onClick={() => openDocument(doc)}
          >
            <span className={i % 2 === 0 ? "pp-page pp-page-chart" : "pp-page pp-page-text"} aria-hidden="true">
              <span className="pp-page-head" />
              <span className="pp-page-line" />
              <span className="pp-page-line" />
              {i % 2 === 0 ? (
                <span className="pp-page-bars">
                  <i style={{ height: "40%" }} />
                  <i style={{ height: "70%" }} />
                  <i style={{ height: "55%" }} />
                  <i style={{ height: "90%" }} />
                </span>
              ) : (
                <>
                  <span className="pp-page-line pp-page-line-short" />
                  <span className="pp-page-line" />
                </>
              )}
            </span>
            <span className="pp-doc-text">
              <span className="pp-doc-title">{doc.title}</span>
              <span className="pp-doc-meta">
                {pageCopy.pdfPages(doc.pageCount)}
                <span className="pp-doc-meta-mobile"> · {pageCopy.viewOnlyShort}</span>
              </span>
              <span className="pp-doc-open">{pageCopy.viewOnly}</span>
            </span>
            <span className="pp-doc-view" aria-hidden="true">
              {pageCopy.view}
            </span>
          </button>
        ))}
      </div>
      {failed ? (
        <p className="pp-unavailable" role="alert">
          {pageCopy.documentUnavailable}
        </p>
      ) : null}
      {open ? <DocumentViewer document={signedPages ? { ...open, pages: signedPages } : open} playerName={playerName} viewerEmail={viewerEmail} onClose={close} /> : null}
    </section>
  );
}
