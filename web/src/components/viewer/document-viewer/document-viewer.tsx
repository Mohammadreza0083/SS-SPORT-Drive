"use client";

import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type PointerEvent } from "react";
import { EmailOverlay } from "@/components/viewer/player/email-overlay";
import { pinchZoom, stepZoom } from "@/lib/viewer/zoom";
import { viewerDocCopy } from "./copy";

/** The document the viewer shows: its rendered pages only, never the PDF file. */
export type ViewerDocument = {
  /** Stable id. */
  id: string;
  /** Display title. */
  title: string;
  /** Number of pages. */
  pageCount: number;
  /** One image URL per page, in order (rendered by ingest, I-4; signed by the edge, V-5b). */
  pages: string[];
};

/** Props of {@link DocumentViewer}. */
type DocumentViewerProps = {
  /** The open document. */
  document: ViewerDocument;
  /** The player the document belongs to, shown in the header. */
  playerName: string;
  /** The signed-in viewer's email, laid faintly over every page (owner, 2026-10-31). */
  viewerEmail?: string;
  /** Closes the viewer. */
  onClose: () => void;
};

/** The phone zoom the zoom button jumps to (canvas `DocViewerMobile`). */
const TAP_ZOOM = 1.6;

/** Elements that can take focus inside the dialog. */
const FOCUSABLE = 'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])';

/**
 * The view-only document viewer (backlog V-6), built from the canvas artboards `DocViewer` and `DocViewerMobile`.
 * Desktop and tablet: a large dialog with page thumbnails, a page counter and zoom from 50 % to 200 %. Phones: a
 * sheet that slides up, one page at a time with Previous / Next, pinch or tap to zoom.
 * Only rendered page images are shown, with no download, no printing, no context menu and no dragging; the viewer's
 * email lies faintly over every page, as on the videos.
 *
 * @param props - See {@link DocumentViewerProps}.
 * @returns The open viewer.
 */
export function DocumentViewer({ document: doc, playerName, viewerEmail, onClose }: DocumentViewerProps) {
  const [page, setPage] = useState(1);
  const [zoom, setZoom] = useState(100);
  const [pinch, setPinch] = useState(1);
  const dialogRef = useRef<HTMLElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const pageRefs = useRef<(HTMLElement | null)[]>([]);
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const pinchStart = useRef<{ distance: number; zoom: number } | null>(null);
  const total = doc.pages.length;

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
     * Escape closes the viewer wherever focus is while it is open.
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

  useEffect(() => {
    // Keeps the counter on the page in view while the viewer scrolls through the stacked pages.
    if (typeof IntersectionObserver === "undefined") {
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
        const index = visible ? pageRefs.current.indexOf(visible.target as HTMLElement) : -1;
        if (index >= 0) {
          setPage(index + 1);
        }
      },
      { threshold: [0.5] },
    );
    pageRefs.current.forEach((element) => element && observer.observe(element));
    return () => observer.disconnect();
  }, [doc.id]);

  /**
   * Shows one page: makes it current and scrolls it into view.
   *
   * @param n - The page number, 1-based.
   */
  const goTo = (n: number) => {
    const target = Math.min(total, Math.max(1, n));
    setPage(target);
    const element = pageRefs.current[target - 1];
    if (element && typeof element.scrollIntoView === "function") {
      element.scrollIntoView({ block: "start", behavior: "smooth" });
    }
  };

  /**
   * Keeps Tab inside the dialog.
   *
   * @param event - The key press.
   */
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === "Tab" && dialogRef.current) {
      const focusable = [...dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((el) => el.offsetParent !== null || el === closeRef.current);
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && window.document.activeElement === first) {
        event.preventDefault();
        last?.focus();
      } else if (!event.shiftKey && window.document.activeElement === last) {
        event.preventDefault();
        first?.focus();
      }
    }
  };

  /**
   * Blocks the browser's context menu ("Save image as…").
   *
   * @param event - The right click or long press.
   */
  const blockMenu = (event: MouseEvent) => event.preventDefault();

  /**
   * Tracks a finger for pinch-to-zoom on phones.
   *
   * @param event - The pointer event.
   */
  const onPointerDown = (event: PointerEvent<HTMLElement>) => {
    pointers.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.current.size === 2) {
      pinchStart.current = { distance: fingerDistance(pointers.current), zoom: pinch };
    }
  };

  /**
   * Zooms while two fingers move apart or together.
   *
   * @param event - The pointer event.
   */
  const onPointerMove = (event: PointerEvent<HTMLElement>) => {
    if (!pointers.current.has(event.pointerId)) {
      return;
    }
    pointers.current.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (pointers.current.size === 2 && pinchStart.current) {
      setPinch(pinchZoom(pinchStart.current.zoom, pinchStart.current.distance, fingerDistance(pointers.current)));
    }
  };

  /**
   * Forgets a lifted finger.
   *
   * @param event - The pointer event.
   */
  const onPointerUp = (event: PointerEvent<HTMLElement>) => {
    pointers.current.delete(event.pointerId);
    if (pointers.current.size < 2) {
      pinchStart.current = null;
    }
  };

  const meta = `${viewerDocCopy.pdfPages(doc.pageCount)}`;
  const lock = (
    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V8a4 4 0 0 1 8 0v3" />
    </svg>
  );

  return (
    <div className="dv-root">
      <div className="dv-backdrop" aria-hidden="true" />
      <section
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-label={viewerDocCopy.dialogLabel(doc.title)}
        className="dv"
        onKeyDown={onKeyDown}
        onContextMenu={blockMenu}
        style={{ "--dv-w": `${Math.round((560 * zoom) / 100)}px`, "--dv-pinch": pinch } as React.CSSProperties}
      >
        <span className="dv-handle" aria-hidden="true" />
        <header className="dv-head">
          <span className="dv-icon" aria-hidden="true">
            <span className="dv-icon-head" />
            <span className="dv-icon-line" />
            <span className="dv-icon-line" />
            <span className="dv-icon-bars">
              <i style={{ height: "40%" }} />
              <i style={{ height: "70%" }} />
              <i style={{ height: "55%" }} />
              <i style={{ height: "90%" }} />
            </span>
          </span>
          <div className="dv-titles">
            <h1 className="dv-title">{doc.title}</h1>
            <div className="dv-meta">
              <span className="dv-player">{playerName}</span>
              <span className="dv-dot dv-player" aria-hidden="true">
                ·
              </span>
              <span>{meta}</span>
              <span className="dv-dot" aria-hidden="true">
                ·
              </span>
              <span className="dv-lock">
                {lock}
                {viewerDocCopy.viewOnly}
              </span>
            </div>
          </div>

          <div className="dv-tools">
            <span className="dv-counter" aria-live="polite">
              <span className="dv-counter-word">{viewerDocCopy.page} </span>
              <b>{page}</b> / {total}
            </span>
            <span className="dv-sep" aria-hidden="true" />
            <button type="button" className="dv-tool" aria-label={viewerDocCopy.zoomOut} onClick={() => setZoom((z) => stepZoom(z, -1))}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
                <path d="M5 12h14" />
              </svg>
            </button>
            <span className="dv-zoom">{zoom}%</span>
            <button type="button" className="dv-tool" aria-label={viewerDocCopy.zoomIn} onClick={() => setZoom((z) => stepZoom(z, 1))}>
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
                <path d="M5 12h14M12 5v14" />
              </svg>
            </button>
            <button type="button" className="dv-tool dv-fit" onClick={() => setZoom(100)}>
              {viewerDocCopy.fit}
            </button>
            <span className="dv-sep" aria-hidden="true" />
          </div>
          <button ref={closeRef} type="button" className="dv-close" aria-label={viewerDocCopy.close} onClick={onClose}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </header>

        <div className="dv-body">
          <nav className="dv-thumbs" aria-label={viewerDocCopy.pages}>
            {doc.pages.map((src, i) => (
              <button
                key={src}
                type="button"
                className={page === i + 1 ? "dv-thumb is-current" : "dv-thumb"}
                aria-label={viewerDocCopy.goTo(i + 1)}
                aria-current={page === i + 1 ? "page" : undefined}
                onClick={() => goTo(i + 1)}
              >
                {/* eslint-disable-next-line @next/next/no-img-element -- signed page images, already sized by ingest */}
                <img src={src} alt="" draggable={false} className="dv-thumb-img" />
                <span className="dv-thumb-n">{i + 1}</span>
              </button>
            ))}
          </nav>

          <div className="dv-pages" onPointerDown={onPointerDown} onPointerMove={onPointerMove} onPointerUp={onPointerUp} onPointerCancel={onPointerUp}>
            {doc.pages.map((src, i) => (
              <figure
                key={src}
                ref={(element) => void (pageRefs.current[i] = element)}
                className={page === i + 1 ? "dv-page is-current" : "dv-page"}
              >
                {/* eslint-disable-next-line @next/next/no-img-element -- signed page images, already sized by ingest */}
                <img src={src} alt={viewerDocCopy.pageOf(i + 1, total)} draggable={false} className="dv-page-img" />
                {viewerEmail ? <EmailOverlay email={viewerEmail} tone="dark" /> : null}
              </figure>
            ))}
            {pinch === 1 ? (
              <div className="dv-hint" aria-hidden="true">
                <svg width="30" height="16" viewBox="0 0 30 16" aria-hidden="true">
                  <circle cx="9" cy="8" r="5" fill="none" stroke="#FF4545" strokeWidth="2" />
                  <circle cx="21" cy="8" r="5" fill="none" stroke="#FF4545" strokeWidth="2" />
                </svg>
                {viewerDocCopy.pinch}
              </div>
            ) : null}
          </div>
        </div>

        <div className="dv-controls">
          <button type="button" className="dv-step" aria-label={viewerDocCopy.previous} disabled={page === 1} onClick={() => goTo(page - 1)}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
              <path d="M15 18l-6-6 6-6" />
            </svg>
            {viewerDocCopy.prev}
          </button>
          <button
            type="button"
            className="dv-tap-zoom"
            aria-label={pinch > 1 ? viewerDocCopy.fitWidth : viewerDocCopy.zoomTo(TAP_ZOOM)}
            onClick={() => setPinch((p) => (p > 1 ? 1 : TAP_ZOOM))}
          >
            {pinch > 1 ? `${Math.round(pinch * 100)}%` : viewerDocCopy.fit}
          </button>
          <button type="button" className="dv-step dv-next" aria-label={viewerDocCopy.next} disabled={page === total} onClick={() => goTo(page + 1)}>
            {viewerDocCopy.nextShort}
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
              <path d="M9 18l6-6-6-6" />
            </svg>
          </button>
        </div>

        <footer className="dv-foot">
          {lock}
          <span className="dv-foot-long">{viewerDocCopy.protectedLong}</span>
          <span className="dv-foot-short">{viewerDocCopy.protectedShort}</span>
        </footer>
      </section>
    </div>
  );
}

/**
 * The distance between the first two tracked fingers.
 *
 * @param points - The tracked pointers.
 * @returns The distance in pixels.
 */
function fingerDistance(points: Map<number, { x: number; y: number }>): number {
  const [a, b] = [...points.values()];
  return a && b ? Math.hypot(a.x - b.x, a.y - b.y) : 0;
}
