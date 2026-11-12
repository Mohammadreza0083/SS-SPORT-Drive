"use client";

import { useId, useState, type CSSProperties } from "react";
import { DriftMark } from "@/components/viewer/backdrop";
import { Chip } from "@/components/viewer/chip";
import { ContactButton, SiteNav } from "@/components/viewer/site-nav";
import { rosterCopy } from "./copy";
import type { RosterData, RosterPlayer } from "./types";

/** Props of {@link RosterPage}. */
type RosterPageProps = {
  /** The link's players. */
  data: RosterData;
  /** The season shown in the eyebrow; the server passes it so server and browser render the same text. */
  year: number;
};

/**
 * The Roster page (backlog V-2b), built from the design canvas's `Roster` artboard: the eyebrow, the "Shared privately
 * with" ribbon, the rising "Roster" heading, position chips that filter the cards in place, and one card per player
 * leading to that player's page on the same link.
 *
 * @param props - See {@link RosterPageProps}.
 * @returns The page content inside the viewer shell.
 */
export function RosterPage({ data, year }: RosterPageProps) {
  const [position, setPosition] = useState<string>(rosterCopy.allPositions);
  const shown = position === rosterCopy.allPositions ? data.players : data.players.filter((player) => player.position === position);

  return (
    <div className="rs-page">
      <DriftMark style={{ right: "-10%", top: "-60px", width: "900px", maxWidth: "90%", opacity: 0.12 }} />
      <SiteNav>
        <ContactButton />
      </SiteNav>

      <div className="rs-inner">
        <header className="rs-head">
          <div>
            <div className="ss-fu rs-eyebrow" style={{ animationDelay: ".1s" }}>
              {rosterCopy.eyebrow(year)}
            </div>
            <div className="ss-ribbon ss-fu rs-ribbon" style={{ animationDelay: ".2s" }}>
              <span className="ss-ribbon-dot" aria-hidden="true" />
              {rosterCopy.sharedWith} <b>{data.sharedWith}</b> · {rosterCopy.playerCount(data.playerCount)}
            </div>
            <h1 className="rs-title">
              <span className="ss-ln">
                <span className="rs-rise">{rosterCopy.pageTitle}</span>
              </span>
            </h1>
          </div>
          <div className="ss-fu rs-chips" role="group" aria-label={rosterCopy.filterLabel} style={{ animationDelay: ".4s" }}>
            {[rosterCopy.allPositions, ...rosterCopy.positions].map((label) => (
              <Chip key={label} pressed={label === position} onClick={() => setPosition(label)}>
                {label}
              </Chip>
            ))}
          </div>
        </header>

        <div className="rs-grid">
          {shown.map((player, index) => (
            <RosterCard key={player.id} player={player} delay={0.2 + index * 0.08} />
          ))}
        </div>
      </div>
    </div>
  );
}

/** Props of {@link RosterCard}. */
type RosterCardProps = {
  /** The player. */
  player: RosterPlayer;
  /** The card's entry delay in seconds (80 ms stagger, as on the canvas). */
  delay: number;
};

/**
 * One player card: position badge, photo or silhouette, country and video count, the name on two lines, and the
 * underline and "View player →" that grow on hover.
 *
 * @param props - See {@link RosterCardProps}.
 * @returns The card.
 */
function RosterCard({ player, delay }: RosterCardProps) {
  const space = player.name.indexOf(" ");
  const name = space > 0 ? `${player.name.slice(0, space)}\n${player.name.slice(space + 1)}` : player.name;

  return (
    <a href={player.href} className="rs-card" style={{ animationDelay: `${delay.toFixed(2)}s` } as CSSProperties}>
      <RosterPhoto src={player.photoUrl} />
      <span className="rs-fade" aria-hidden="true" />
      <span className="rs-pos">{player.position}</span>
      <span className="rs-body">
        <span className="rs-meta">
          {player.nationality} · {rosterCopy.videoCount(player.videos)}
        </span>
        <span className="rs-name">{name}</span>
        <span className="rs-foot">
          <span className="rs-under" aria-hidden="true" />
          <span className="rs-go">{rosterCopy.viewPlayer}</span>
        </span>
      </span>
    </a>
  );
}

/**
 * A card's photo, or the canvas silhouette while there is none.
 *
 * @param props - The photo URL, if any.
 * @param props.src - The photo URL.
 * @returns The image.
 */
function RosterPhoto({ src }: { src?: string }) {
  const gradientId = useId();
  if (src) {
    // A plain <img>: photos come through the same-origin photo route and are already sized by ingest.
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={src} alt="" className="rs-photo" draggable={false} loading="lazy" />;
  }

  return (
    <svg className="rs-photo" viewBox="0 0 120 150" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#e6cdb9" />
          <stop offset="1" stopColor="#2a1a16" />
        </linearGradient>
      </defs>
      <g fill={`url(#${gradientId})`}>
        <ellipse cx="62" cy="34" rx="17" ry="20" />
        <path d="M54 52 h16 l2 10 C96 66 112 80 116 110 L120 150 H4 L8 110 C12 80 28 66 52 62 Z" />
      </g>
    </svg>
  );
}
