"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type CSSProperties } from "react";
import { DriftMark, Glow } from "@/components/viewer/backdrop";
import { viewerCopy } from "@/components/viewer/copy";
import { SiteFooter } from "@/components/viewer/site-footer";
import { SiteNav } from "@/components/viewer/site-nav";
import { Ticker } from "@/components/viewer/ticker";
import { publicAccessClient } from "@/lib/viewer/public-access";
import { publicCopy } from "./copy";
import { LockIcon, PlayerPhoto } from "./player-silhouette";
import { RequestAccessModal } from "./request-access-modal";
import type { LockedData, RequestAccessClient } from "./types";

/** Props of {@link LockedPlayerPage}. */
type LockedPlayerPageProps = {
  /** The player's public facts and the titles of their locked media. */
  data: LockedData;
  /** The browser side of the request-access flow; previews and tests pass their own. */
  client?: RequestAccessClient;
};

/** Thumbnail tints of the locked videos, from the canvas, repeated when there are more videos. */
const THUMB_TINTS = [
  ["#3a1010", "#120808"],
  ["#21324a", "#0d141d"],
  ["#1e3a33", "#0a1411"],
  ["#2e2240", "#0e0b14"],
  ["#26262b", "#0c0c0f"],
] as const;

/**
 * The locked player page (backlog P-2, P-3 UI), built from the design canvas's `Locked` artboard: the player's public
 * facts, then blurred and locked videos and documents, and the request-access dialog. Nothing here can carry a media
 * address: videos are a title, tag and length over a tint, documents a title and page count.
 *
 * @param props - See {@link LockedPlayerPageProps}.
 * @returns The page content inside the viewer shell.
 */
export function LockedPlayerPage({ data, client = publicAccessClient }: LockedPlayerPageProps) {
  const { player, videos, documents, viewer } = data;
  const [open, setOpen] = useState(false);
  const opener = useRef<HTMLElement | null>(null);
  const [firstName, ...rest] = player.name.split(" ");
  const positionName = publicCopy.positionNames[player.position] ?? player.position;

  const openDialog = useCallback(() => {
    opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setOpen(true);
  }, []);

  const closeDialog = useCallback(() => {
    setOpen(false);
    opener.current?.focus();
  }, []);

  useEffect(() => {
    if (!open) {
      return;
    }

    /**
     * Closes the dialog on Escape.
     *
     * @param event - The key press.
     */
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        closeDialog();
      }
    };

    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, closeDialog]);

  return (
    <div className="lk-page">
      <Glow />
      <DriftMark />
      <div className="lk-inner">
        <SiteNav logoHref="/">
          <Link href="/" className="lk-back">
            {publicCopy.backToSearch}
          </Link>
          <RequestButton small onOpen={openDialog} />
        </SiteNav>

        <section className="lk-hero">
          <div>
            <div className="ss-fu lk-eyebrow">
              <span>{positionName.toUpperCase()}</span>
              <span aria-hidden="true">/</span>
              <span className="lk-bars" aria-hidden="true">
                <i />
                <i />
                <i />
              </span>
              <span>{player.nationality.toUpperCase()}</span>
            </div>
            <h1 className="lk-name" aria-label={player.name}>
              <span className="lk-ln" aria-hidden="true">
                {firstName}
              </span>
              <span className="lk-ln" aria-hidden="true">
                {rest.join(" ")}
              </span>
            </h1>
            <div className="lk-under" aria-hidden="true" />
            <div className="ss-fu lk-actions">
              <RequestButton onOpen={openDialog} withIcon />
              {player.volleyboxUrl ? (
                <a href={player.volleyboxUrl} target="_blank" rel="noopener noreferrer" className="ss-btn ss-btn-ghost">
                  {publicCopy.volleybox}
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
                    <path d="M7 17L17 7M9 7h8v8" />
                  </svg>
                </a>
              ) : null}
            </div>
          </div>
          <div className="lk-side">
            <PlayerPhoto src={player.photoUrl} className="lk-photo" />
            <div className="ss-fu lk-private">
              <b>
                <LockIcon size={13} />
                {publicCopy.privateFootage}
              </b>
              <div>{publicCopy.sharedByLink(player.videos, player.documents)}</div>
            </div>
          </div>
        </section>
      </div>

      <Ticker items={[player.name, positionName, player.nationality, `${player.videos} Videos`, viewerCopy.slogan]} />

      <div className="lk-inner">
        <section className="lk-stats">
          <Stat value={String(player.videos)} label={publicCopy.statVideos} />
          <Stat value={String(player.minutes)} label={publicCopy.statMinutes} />
          <Stat value={String(player.documents)} label={publicCopy.statDocuments} />
          <Stat value={player.position} label={publicCopy.statPosition} />
        </section>

        <section className="lk-section">
          <div className="lk-section-head">
            <h2>{publicCopy.videosTitle}</h2>
            <span className="lk-lockbadge">
              <LockIcon size={12} />
              {publicCopy.locked}
            </span>
          </div>
          <div className="lk-videos">
            <div className="lk-stage">
              <div className="lk-blur" aria-hidden="true" />
              <div className="lk-stage-body">
                <span className="lk-lock">
                  <LockIcon size={34} />
                </span>
                <div className="lk-stage-title">{publicCopy.footageIsPrivate}</div>
                <p>{publicCopy.footageHelp}</p>
                <RequestButton onOpen={openDialog} />
              </div>
            </div>
            <div className="lk-list">
              {videos.map((video, index) => {
                const [a, b] = THUMB_TINTS[index % THUMB_TINTS.length];
                return (
                  <div key={`${video.title}-${index}`} className="lk-item">
                    <span className="lk-thumb" style={{ "--thumb-a": a, "--thumb-b": b } as CSSProperties}>
                      <LockIcon size={18} />
                    </span>
                    <span>
                      <span className="lk-item-title">{video.title}</span>
                      <span className="lk-item-meta">
                        <span>{video.tag}</span>
                        <span>{video.duration}</span>
                      </span>
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </section>

        <section className="lk-section">
          <div className="lk-section-head">
            <h2>{publicCopy.documentsTitle}</h2>
          </div>
          <div className="lk-docs">
            {documents.map((document, index) => (
              <button key={`${document.title}-${index}`} type="button" className="lk-doc" onClick={openDialog}>
                <span className="lk-doc-page" aria-hidden="true" />
                <span className="lk-doc-text">
                  <span className="lk-doc-name">{document.title}</span>
                  <span className="lk-doc-meta">{publicCopy.pdfLine(document.pages)}</span>
                </span>
              </button>
            ))}
          </div>
        </section>

        <section>
          <div className="lk-cta">
            <div>
              <div className="lk-cta-title">{publicCopy.ctaTitle(firstName)}</div>
              <p>
                {publicCopy.ctaText} <a href={`mailto:${viewerCopy.contactEmail}`}>{viewerCopy.contactEmail}</a>.
              </p>
            </div>
            <RequestButton onOpen={openDialog} />
          </div>
        </section>

        <SiteFooter>
          <a href={`mailto:${viewerCopy.contactEmail}`}>{viewerCopy.contactEmail}</a> · <a href="#privacy">{viewerCopy.privacy}</a>
        </SiteFooter>
      </div>

      {open ? <RequestAccessModal player={player} viewer={viewer} client={client} onClose={closeDialog} /> : null}
    </div>
  );
}

/**
 * One stat tile (the counts are public).
 *
 * @param props - The value and its label.
 * @param props.value - The number or code.
 * @param props.label - What it counts.
 * @returns The tile.
 */
function Stat({ value, label }: { value: string; label: string }) {
  return (
    <div className="lk-stat">
      <div>{value}</div>
      <div>{label}</div>
    </div>
  );
}

/** Props of {@link RequestButton}. */
type RequestButtonProps = {
  /** Opens the request-access dialog. */
  onOpen: () => void;
  /** The smaller nav size. */
  small?: boolean;
  /** Shows the padlock before the label. */
  withIcon?: boolean;
};

/**
 * A red "Request access" button.
 *
 * @param props - See {@link RequestButtonProps}.
 * @returns The button.
 */
function RequestButton({ onOpen, small = false, withIcon = false }: RequestButtonProps) {
  return (
    <button type="button" className={small ? "ss-btn ss-btn-red ss-btn-sm" : "ss-btn ss-btn-red"} onClick={onOpen}>
      {withIcon ? <LockIcon size={16} /> : null}
      {publicCopy.requestAccess}
    </button>
  );
}
