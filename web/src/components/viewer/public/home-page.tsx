"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { DriftMark, Glow } from "@/components/viewer/backdrop";
import { Chip } from "@/components/viewer/chip";
import { viewerCopy } from "@/components/viewer/copy";
import { NavLink, SiteNav } from "@/components/viewer/site-nav";
import { SiteFooter } from "@/components/viewer/site-footer";
import { publicCopy } from "./copy";
import { LockIcon, PlayerPhoto } from "./player-silhouette";
import { POSITION_FILTERS, filterPlayers, homeHref, resultCountLabel } from "./search";
import type { HomeData, PublicPlayer, PublicViewer } from "./types";

/** Props of {@link HomePage}. */
type HomePageProps = {
  /** The searchable players and the signed-in viewer, if any. */
  data: HomeData;
};

/** How long to wait after the last keystroke before the search runs on the server. */
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The public home page on a fixed list of players (the design preview and the tests): typing and the chips filter the
 * list in the browser. The real page uses {@link RemoteHomePage}.
 *
 * @param props - See {@link HomePageProps}.
 * @returns The page content inside the viewer shell.
 */
export function HomePage({ data }: HomePageProps) {
  const [query, setQuery] = useState("");
  const [position, setPosition] = useState<string>("All");
  const shown = useMemo(() => filterPlayers(data.players, query, position), [data.players, query, position]);

  return <HomeView data={data} players={shown} total={shown.length} query={query} onQuery={setQuery} position={position} onPosition={setPosition} />;
}

/**
 * The public home page on real data (backlog P-1, P-7, P-8): the API searches and filters in SQL, one page at a time.
 * Typing (after a short pause) and the chips replace the address, so the server renders the new page; the pager is
 * plain links.
 *
 * @param props - See {@link HomePageProps}; the data holds the page of the search.
 * @returns The page content inside the viewer shell.
 */
export function RemoteHomePage({ data }: HomePageProps) {
  const router = useRouter();
  const position = data.position ?? "All";
  const current = data.query ?? "";
  const [query, setQuery] = useState(current);

  useEffect(() => {
    if (query.trim() === current) {
      return;
    }

    const timer = setTimeout(() => router.replace(homeHref(query, position, 1)), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [query, current, position, router]);

  const page = data.page ?? 1;
  const pages = data.totalPages ?? 1;
  return (
    <HomeView
      data={data}
      players={data.players}
      total={data.totalCount ?? data.players.length}
      query={query}
      onQuery={setQuery}
      position={position}
      onPosition={(next) => router.replace(homeHref(query, next, 1))}
      pager={{ page, pages, href: (target) => homeHref(current, position, target) }}
    />
  );
}

/** Props of {@link HomeView}. */
type HomeViewProps = {
  /** The page's data (for the signed-in viewer). */
  data: HomeData;
  /** The players to show. */
  players: PublicPlayer[];
  /** How many players match in all, for the count next to the search box. */
  total: number;
  /** The text in the search box. */
  query: string;
  /** Called when the visitor types. */
  onQuery: (query: string) => void;
  /** The pressed position chip. */
  position: string;
  /** Called when a chip is pressed. */
  onPosition: (position: string) => void;
  /** The pager, when the results span pages. */
  pager?: { page: number; pages: number; href: (page: number) => string };
};

/**
 * The look of the public home page, built from the design canvas's `Home` artboard: the hero, the player search with
 * position chips, one card per player leading to the locked player page, and "How access works". The nav shows Sign in,
 * or the signed-in viewer's email with Sign out.
 *
 * @param props - See {@link HomeViewProps}.
 * @returns The page content inside the viewer shell.
 */
function HomeView({ data, players, total, query, onQuery, position, onPosition, pager }: HomeViewProps) {
  return (
    <div className="ph-page">
      <Glow style={{ left: "50%", top: "-420px", width: "1100px", height: "900px", marginLeft: "-550px" }} />
      <DriftMark style={{ right: "-6%", top: "40px", width: "760px", maxWidth: "70%", opacity: 0.14 }} />

      <div className="ph-inner">
        <SiteNav>
          <NavLink href="#how">{publicCopy.howLink}</NavLink>
          <ViewerMenu viewer={data.viewer} />
        </SiteNav>

        <section className="ph-hero">
          <div className="ss-fu ph-eyebrow" style={{ animationDelay: ".15s" }}>
            {publicCopy.eyebrow}
          </div>
          <h1 className="ss-fu ph-title" style={{ animationDelay: ".3s" }}>
            {publicCopy.title}
          </h1>
          <p className="ss-fu ph-lead" style={{ animationDelay: ".45s" }}>
            {publicCopy.lead}
          </p>

          <div className="ss-fu ph-search" style={{ animationDelay: ".6s" }}>
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#8A93A6" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
              <circle cx="11" cy="11" r="7" />
              <path d="M20 20l-3.5-3.5" />
            </svg>
            <label htmlFor="ph-q" className="ph-sr-only">
              {publicCopy.searchLabel}
            </label>
            <input
              id="ph-q"
              type="search"
              value={query}
              onChange={(event) => onQuery(event.target.value)}
              placeholder={publicCopy.searchPlaceholder}
              autoComplete="off"
              // Playwright's caret hiding adds a style to the input before React hydrates it; nothing else differs.
              suppressHydrationWarning
            />
            <span className="ph-count">{resultCountLabel(total)}</span>
          </div>
          <div className="ss-fu ph-chips" role="group" aria-label={publicCopy.filterLabel} style={{ animationDelay: ".75s" }}>
            {POSITION_FILTERS.map((label) => (
              <Chip key={label} pressed={label === position} onClick={() => onPosition(label)}>
                {label}
              </Chip>
            ))}
          </div>
        </section>

        <section aria-live="polite" className="ph-results">
          {players.length > 0 ? (
            <div className="ph-grid">
              {players.map((player, index) => (
                <PlayerCard key={player.slug} player={player} delay={0.1 + index * 0.07} />
              ))}
            </div>
          ) : (
            <div className="ph-empty">
              <div className="ph-empty-title">{publicCopy.noMatch(query)}</div>
              <p>
                {publicCopy.noMatchHelp} <a href={`mailto:${viewerCopy.contactEmail}`}>{viewerCopy.contactEmail}</a>.
              </p>
            </div>
          )}
          {pager && pager.pages > 1 ? <Pager page={pager.page} pages={pager.pages} href={pager.href} /> : null}
        </section>

        <section id="how" className="ph-how">
          <h2>{publicCopy.howTitle}</h2>
          <div className="ph-steps">
            {publicCopy.steps.map((step, index) => (
              <div key={step.number} className={index === 2 ? "ph-step ph-step-hot" : "ph-step"}>
                <div className="ph-step-number">{step.number}</div>
                <div className="ph-step-title">{step.title}</div>
                <p>{step.text}</p>
              </div>
            ))}
          </div>
        </section>

        <SiteFooter>
          <a href={`mailto:${viewerCopy.contactEmail}`}>{viewerCopy.contactEmail}</a> · <a href="#privacy">{viewerCopy.privacy}</a> · {viewerCopy.slogan}
        </SiteFooter>
      </div>
    </div>
  );
}

/**
 * The right-hand end of the nav: Sign in for a visitor; for a signed-in viewer their email and a Sign out button.
 * Sign out is a POST form so a link crawler or prefetch can never sign anyone out.
 *
 * @param props - The viewer, if signed in.
 * @param props.viewer - The signed-in viewer, or null.
 * @returns The sign-in link or the viewer chip.
 */
function ViewerMenu({ viewer }: { viewer: PublicViewer }) {
  if (!viewer) {
    return (
      <a href={publicCopy.signInHref} className="ph-signin">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
          <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4M10 17l5-5-5-5M15 12H3" />
        </svg>
        {publicCopy.signIn}
      </a>
    );
  }

  return (
    <span className="ph-viewer">
      <span className="ph-viewer-dot" aria-hidden="true" />
      <span className="ph-viewer-email">{viewer.email}</span>
      <form method="post" action={publicCopy.signOutAction}>
        <button type="submit">{publicCopy.signOut}</button>
      </form>
    </span>
  );
}

/** Props of {@link PlayerCard}. */
type PlayerCardProps = {
  /** The player. */
  player: PublicPlayer;
  /** The card's entry delay in seconds. */
  delay: number;
};

/**
 * One player card: position badge, "Private" badge, photo or silhouette, nationality, the name on two lines and the
 * counts. It leads to the player's locked page.
 *
 * @param props - See {@link PlayerCardProps}.
 * @returns The card.
 */
function PlayerCard({ player, delay }: PlayerCardProps) {
  const space = player.name.indexOf(" ");
  const name = space > 0 ? `${player.name.slice(0, space)}\n${player.name.slice(space + 1)}` : player.name;

  return (
    <a
      href={`/players/${player.slug}`}
      aria-label={publicCopy.viewPlayer(player.name)}
      className="ph-card"
      style={{ animationDelay: `${delay.toFixed(2)}s` } as CSSProperties}
    >
      <PlayerPhoto src={player.photoUrl} className="ph-photo" />
      <span className="ph-fade" aria-hidden="true" />
      <span className="ph-pos">{player.position}</span>
      <span className="ph-badge">
        <LockIcon size={12} />
        {publicCopy.private}
      </span>
      <span className="ph-body">
        <span className="ph-nat">{player.nationality}</span>
        <span className="ph-name">{name}</span>
        <span className="ph-foot">
          <span className="ph-meta">{publicCopy.counts(player.videos, player.documents)}</span>
          <span className="ph-go">{publicCopy.go}</span>
        </span>
      </span>
    </a>
  );
}

/** Props of {@link Pager}. */
type PagerProps = {
  /** The current page, from 1. */
  page: number;
  /** How many pages there are. */
  pages: number;
  /** The address of a page. */
  href: (page: number) => string;
};

/**
 * Previous and next links under the results. They are plain links, so every page is its own address.
 *
 * @param props - See {@link PagerProps}.
 * @returns The pager.
 */
function Pager({ page, pages, href }: PagerProps) {
  return (
    <nav className="ph-pager" aria-label={publicCopy.pagerLabel}>
      {page > 1 ? (
        <a href={href(page - 1)} rel="prev">
          {publicCopy.previous}
        </a>
      ) : (
        <span aria-hidden="true" />
      )}
      <span>{publicCopy.pageOf(page, pages)}</span>
      {page < pages ? (
        <a href={href(page + 1)} rel="next">
          {publicCopy.next}
        </a>
      ) : (
        <span aria-hidden="true" />
      )}
    </nav>
  );
}
