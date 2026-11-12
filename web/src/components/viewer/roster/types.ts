/** What the Roster page (backlog V-2b, canvas `Roster`) needs: the link's players as cards. */
export type RosterData = {
  /** Who the link was shared with: their club or agency, else their name. */
  sharedWith: string;
  /** How many players the link holds. */
  playerCount: number;
  /** The players, in the link's page order. */
  players: RosterPlayer[];
};

/** One player card on the roster. */
export type RosterPlayer = {
  /** Stable id. */
  id: string;
  /** Where the card leads: the player's page on this link. */
  href: string;
  /** Full name; the first word goes on its own line. */
  name: string;
  /** Primary position code (S, OH, OPP, MB, L, DS). */
  position: string;
  /** Nationality, in English. */
  nationality: string;
  /** How many videos can be watched. */
  videos: number;
  /** The cut-out photo; the canvas silhouette is shown while missing. */
  photoUrl?: string;
};
