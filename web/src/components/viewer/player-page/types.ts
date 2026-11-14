import type { PlayerScout } from "@/lib/viewer/scout";

/**
 * What the player page (backlog V-2) needs to render. On `/r/{code}` it is built from the viewer API (V-2b,
 * `lib/viewer/content-server.ts`); the design preview uses {@link "./sample"}.
 */
export type PlayerPageData = {
  /** The player shown on the page. */
  player: {
    /** Full name as stored (Latin letters), e.g. "Arash Tehrani". */
    name: string;
    /** First name, used in "Interested in {first name}?". */
    firstName: string;
    /** Primary position. */
    position: { code: string; label: string };
    /** Nationality: ISO 3166-1 alpha-2 code (or XK) and English name. */
    nationality: { code: string; name: string };
    /** The player's Volleybox profile. */
    volleyboxUrl: string;
    /** Cut-out photo on a transparent background; a silhouette is shown while missing. */
    photoUrl?: string;
  };
  /** The signed-in viewer's email, laid faintly over the videos (V-4). */
  viewerEmail?: string;
  /** Who the personal link was prepared for: their club or agency when known, and their name. */
  recipient?: { organisation?: string; name: string };
  /** The personal note an admin wrote on the link. */
  note?: { text: string; author: string };
  /** Totals shown in the stats row; `matches` counts the DataVolley files (V-8) and is shown when above zero. */
  stats: { videos: number; minutes: number; documents: number; matches?: number };
  /** The visible videos, in their display order. */
  videos: PlayerVideo[];
  /** The visible documents (view-only PDFs). */
  documents: PlayerDocument[];
  /** The player's DataVolley scout files (V-8); the "Match stats" section is left out when there are none. */
  scouts?: PlayerScout[];
};

/** One video on the page. */
export type PlayerVideo = {
  /** Stable id. */
  id: string;
  /** Display title. */
  title: string;
  /** Tag shown on the chips and labels (Highlight, Spike, Serve, Block, Full match, …). */
  tag: string;
  /** Length in seconds. */
  durationSeconds: number;
  /** HLS master playlist for fixed sources (the design preview). Empty on `/r/{code}`, where `streamPath` signs it. */
  src: string;
  /** The same-origin route that signs this video's stream for the viewer (V-5b); the player asks for it when the video is shown. */
  streamPath?: string;
  /** The same-origin route the player reports this video's watching to (W-2); absent for sample videos, which track nothing. */
  watchPath?: string;
  /** Thumbnail image; a tinted gradient is shown while missing. */
  thumbnailUrl?: string;
  /** Frame rate, for frame stepping. */
  fps?: number;
};

/** One view-only document on the page. */
export type PlayerDocument = {
  /** Stable id. */
  id: string;
  /** Display title. */
  title: string;
  /** Number of pages. */
  pageCount: number;
  /** One image URL per page, in order: fixed pages (the design preview), or empty on `/r/{code}`, where `pagesPath` signs them. The PDF file itself never reaches the browser. */
  pages: string[];
  /** The same-origin route that signs this document's page images for the viewer (V-5b); asked for when the document is opened. */
  pagesPath?: string;
};
