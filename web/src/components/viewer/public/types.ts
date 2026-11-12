import type { CodeRequest } from "@/lib/viewer/gate";

/** One player on the public home page (backlog P-1): only facts the public may see. */
export type PublicPlayer = {
  /** URL-safe name; the card leads to `/players/{slug}`. */
  slug: string;
  /** Full name; the first word goes on its own line on cards. */
  name: string;
  /** Primary position code (S, OH, OPP, MB, L, DS). */
  position: string;
  /** Nationality, in English. */
  nationality: string;
  /** How many videos exist (the videos themselves stay private). */
  videos: number;
  /** How many documents exist. */
  documents: number;
  /** The cut-out photo on this site's own origin; the canvas silhouette is shown while missing. */
  photoUrl?: string;
};

/** The signed-in viewer (P-7), or `null` for a visitor. */
export type PublicViewer = { email: string } | null;

/** What the public home page needs. */
export type HomeData = {
  /** The players that can be searched. */
  players: PublicPlayer[];
  /** Who is signed in, if anyone. */
  viewer: PublicViewer;
};

/** A locked video: its title, tag and length only. It has no address, stream or thumbnail (P-2). */
export type LockedVideo = {
  /** The video's title. */
  title: string;
  /** Its first tag, or "Video". */
  tag: string;
  /** Length such as `1:24` or `1:48:10`. */
  duration: string;
};

/** A locked document: its title and page count only. */
export type LockedDocument = {
  /** The document's title. */
  title: string;
  /** Number of pages. */
  pages: number;
};

/** What the locked player page needs (P-2). There is deliberately no field that could carry a media address. */
export type LockedData = {
  /** The player and the public facts about their footage. */
  player: PublicPlayer & {
    /** Total minutes of footage. */
    minutes: number;
    /** The player's Volleybox profile, if known. */
    volleyboxUrl?: string;
  };
  /** The locked videos. */
  videos: LockedVideo[];
  /** The locked documents. */
  documents: LockedDocument[];
  /** Who is signed in, if anyone. */
  viewer: PublicViewer;
};

/** What the visitor sends to ask for access. */
export type AccessRequest = {
  /** The player they want to see. */
  playerSlug: string;
  /** Club or agency, role, and what they are looking for. */
  about: string;
  /** The verified email; left out for a signed-in viewer. */
  email?: string;
  /** The six-digit code; left out for a signed-in viewer. */
  code?: string;
  /** The Cloudflare Turnstile token, when the page has one. */
  turnstileToken: string | null;
};

/** The outcome of sending an access request. */
export type AccessSubmit =
  | { kind: "sent" }
  | { kind: "refused"; code: string | null; message: string | null; attemptsLeft: number | null };

/** The browser side of the request-access flow; tests and previews pass their own. */
export type RequestAccessClient = {
  /** Asks for a six-digit code for an email. */
  requestCode(email: string): Promise<CodeRequest>;
  /** Sends the request (with the code, for a signed-out visitor). */
  submit(request: AccessRequest): Promise<AccessSubmit>;
};
