/**
 * What the player page (backlog V-2) needs to render. The API fills it for a personal link once L-1 lands;
 * until then the design preview uses {@link "./sample"}.
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
  /** Who the personal link was prepared for; absent on links without a named recipient. */
  recipient?: { organisation: string; name: string };
  /** The personal note an admin wrote on the link. */
  note?: { text: string; author: string };
  /** Totals shown in the stats row. */
  stats: { videos: number; minutes: number; documents: number };
  /** The visible videos, in their display order. */
  videos: PlayerVideo[];
  /** The visible documents (view-only PDFs). */
  documents: PlayerDocument[];
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
  /** HLS master playlist (signed once V-5 lands). */
  src: string;
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
  /** Where the in-page viewer opens (V-6). */
  href: string;
};
