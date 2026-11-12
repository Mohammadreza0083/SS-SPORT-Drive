import { DriftMark, Glow } from "@/components/viewer/backdrop";
import { ButtonLink } from "@/components/viewer/button";
import { viewerCopy } from "@/components/viewer/copy";
import { CountUp } from "@/components/viewer/count-up";
import { NameReveal } from "@/components/viewer/name-reveal";
import { ContactButton, NavLink, SiteNav } from "@/components/viewer/site-nav";
import { SiteFooter } from "@/components/viewer/site-footer";
import { Spotlight } from "@/components/viewer/spotlight";
import { Ticker } from "@/components/viewer/ticker";
import { pageCopy } from "./copy";
import { Flag } from "./flag";
import { DocumentsSection } from "./documents-section";
import { tickerItems } from "./format";
import { PlayerPhoto } from "./player-photo";
import type { PlayerPageData } from "./types";
import { VideosSection } from "./videos-section";

/** Props of {@link PlayerPage}. */
type PlayerPageProps = {
  /** Everything the page shows. */
  data: PlayerPageData;
};

/**
 * The player page (backlog V-2), built from the design canvas's Main, Tablet and Mobile artboards:
 * hero, ticker, note and stats, videos, documents, the email-only contact panel and the footer.
 * Layout differences between breakpoints live in `player-page.css`; elements that move between places
 * (the "Prepared for" ribbon, the note) are rendered in each place and shown only where they belong.
 *
 * @param props - See {@link PlayerPageProps}.
 * @returns The page content inside the viewer shell.
 */
export function PlayerPage({ data }: PlayerPageProps) {
  const { player, recipient, note, stats, documents } = data;
  const preparedFor = recipient ? (
    <>
      {pageCopy.preparedFor} <b>{recipient.organisation ?? recipient.name}</b>
      {recipient.organisation ? <> · {recipient.name}</> : null}
    </>
  ) : null;

  return (
    <>
      <Spotlight className="pp-hero">
        <Glow />
        <DriftMark />
        <SiteNav>
          <NavLink href="#videos">{pageCopy.videosNav}</NavLink>
          {documents.length > 0 ? <NavLink href="#docs">{pageCopy.documentsNav}</NavLink> : null}
          <ContactButton />
        </SiteNav>

        <div className="pp-hero-inner">
          <div className="pp-hero-text">
            {preparedFor ? (
              <div className="ss-ribbon ss-fu pp-ribbon" style={{ animationDelay: ".5s" }}>
                <span className="ss-ribbon-dot" aria-hidden="true" />
                <span>{preparedFor}</span>
              </div>
            ) : null}
            <div className="ss-eyebrow ss-fu pp-eyebrow" style={{ animationDelay: ".7s" }}>
              <span className="pp-pos-long">{player.position.label.toUpperCase()}</span>
              <span className="pp-pos-short">{player.position.code}</span>
              <span className="pp-slash" aria-hidden="true">
                /
              </span>
              <Flag code={player.nationality.code} />
              <span>{player.nationality.name.toUpperCase()}</span>
            </div>
            <NameReveal name={player.name} />
            <div className="ss-under pp-under" />
            <div className="pp-actions">
              {preparedFor ? (
                <div className="ss-fu pp-ribbon-mobile" style={{ animationDelay: "1.5s" }}>
                  {preparedFor}
                </div>
              ) : null}
              <ButtonLink href="#videos" className="ss-fu ss-press pp-watch" style={{ animationDelay: "1.7s" }}>
                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                  <path d="M7 4.5v15l13-7.5z" />
                </svg>
                {pageCopy.watchHighlights}
              </ButtonLink>
              <ButtonLink
                href={player.volleyboxUrl}
                variant="ghost"
                target="_blank"
                rel="noopener noreferrer"
                className="ss-fu ss-press pp-volleybox"
                style={{ animationDelay: "1.85s" }}
              >
                {pageCopy.volleybox}
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
                  <path d="M7 17L17 7M9 7h8v8" />
                </svg>
              </ButtonLink>
            </div>
          </div>

          <div className="pp-hero-photo">
            <PlayerPhoto src={player.photoUrl} name={player.name} />
            {note ? (
              <aside className="pp-note-float ss-fu ss-floaty" style={{ animationDelay: "2s" }} aria-label={pageCopy.noteTitle}>
                <div className="pp-note-title">{pageCopy.noteTitle}</div>
                <div className="pp-note-text">{note.text}</div>
                <div className="pp-note-author">{note.author}</div>
              </aside>
            ) : null}
          </div>
        </div>
        <div className="pp-hero-fade" aria-hidden="true" />
      </Spotlight>

      <Ticker items={tickerItems(data)} />

      <section className="pp-stats">
        {note ? (
          <div className="pp-note-card ss-sr">
            <span className="pp-note-bar" aria-hidden="true" />
            <div>
              <div className="pp-note-title">{pageCopy.noteTitle}</div>
              <div className="pp-note-text">
                {note.text} <span className="pp-note-author">{note.author}</span>
              </div>
            </div>
          </div>
        ) : null}
        <div className="pp-stat ss-panel ss-sr">
          <div className="ss-stat-value">
            <CountUp value={stats.videos} />
          </div>
          <div className="ss-stat-label">{pageCopy.statVideos}</div>
        </div>
        <div className="pp-stat ss-panel ss-sr">
          <div className="ss-stat-value">
            <CountUp value={stats.minutes} />
          </div>
          <div className="ss-stat-label">
            <span className="pp-label-long">{pageCopy.statMinutes}</span>
            <span className="pp-label-short">{pageCopy.statMinutesShort}</span>
          </div>
        </div>
        <div className="pp-stat ss-panel ss-sr">
          <div className="ss-stat-value">
            <CountUp value={stats.documents} />
          </div>
          <div className="ss-stat-label">{pageCopy.statDocuments}</div>
        </div>
        <div className="pp-stat ss-panel ss-panel-hot ss-sr">
          <div className="ss-stat-value pp-stat-position">{player.position.code}</div>
          <div className="ss-stat-label">{pageCopy.statPosition}</div>
        </div>
      </section>

      <VideosSection videos={data.videos} viewerEmail={data.viewerEmail} />

      {documents.length > 0 ? (
        <DocumentsSection documents={documents} playerName={player.name} viewerEmail={data.viewerEmail} />
      ) : null}

      <section id="contact" className="pp-contact">
        <div className="pp-cta ss-cta ss-sr">
          <DriftMark style={{ right: -60, top: -40, width: 520, maxWidth: "none", opacity: 0.25 }} />
          <div className="pp-cta-text">
            <div className="pp-cta-title">
              {pageCopy.interestedLine1}
              <br className="pp-cta-break" /> {pageCopy.interestedLine2(player.firstName)}
            </div>
            <div className="pp-cta-line">{pageCopy.contactLine}</div>
          </div>
          <ButtonLink href={`mailto:${viewerCopy.contactEmail}`} variant="light" className="ss-press pp-cta-button">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <rect x="3" y="5" width="18" height="14" rx="2" />
              <path d="M3 7l9 6 9-6" />
            </svg>
            {viewerCopy.contactEmail}
          </ButtonLink>
        </div>
      </section>

      <SiteFooter>
        {recipient ? <>{pageCopy.sharedWith(recipient.organisation ?? recipient.name)} · </> : null}
        <a href="#contact">{pageCopy.privacy}</a>
      </SiteFooter>
    </>
  );
}
