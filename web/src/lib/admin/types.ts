/**
 * The shapes of what the API's `/admin/*` endpoints answer (the .NET DTOs, camelCase). They are written by hand and
 * checked by the console's pages; the API's OpenAPI document at `/scalar` is the source of truth.
 */

/** A page of an offset list. */
export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number };

/** A page of a cursor list. */
export type Cursored<T> = { items: T[]; nextCursor: string | null };

/** The dashboard's numbers. */
export type DashboardData = {
  period: string;
  kpis: {
    livePlayers: number;
    views: number;
    previousViews: number;
    uniqueViewers: number;
    averageCompletion: number | null;
    pendingAccessRequests: number;
    waitingPlayers: number;
  };
  viewsByDay: { date: string; views: number }[];
  needsAttention: {
    accessRequests: number;
    waitingPlayers: number;
    failedFiles: number;
    failedEmails: number;
    retryingEmails: number;
    lostFolders: number;
  };
  topPlayers: { playerId: string; name: string; views: number; averageCompletion: number }[];
};

/** One watch in the live activity feed. */
export type ActivityRow = {
  watchId: string;
  viewerId: string;
  email: string;
  organisation: string | null;
  playerId: string;
  playerName: string;
  videoTitle: string;
  completion: number;
  lastSeenAt: string;
  isLive: boolean;
};

/** One row of the players list. */
export type PlayerRow = {
  id: string;
  name: string | null;
  positions: string[];
  nationalityCode: string | null;
  nationalityName: string | null;
  videoCount: number;
  documentCount: number;
  viewers: number;
  lastViewedAt: string | null;
  analystName: string | null;
  status: "Live" | "Processing" | "Attention" | "Waiting";
  hasPhoto: boolean;
  volleyboxId: number | null;
  createdAt: string;
};

/** A Drive folder of a player. */
export type FolderRow = { id: string; name: string; access: string; lastSyncedAt: string | null; accessLostAt: string | null };

/** One player in full. */
export type PlayerDetail = {
  id: string;
  name: string | null;
  slug: string | null;
  positions: string[];
  nationality: { code: string; name: string } | null;
  volleyboxId: number | null;
  volleyboxUrl: string | null;
  hasPhoto: boolean;
  analystId: string | null;
  analystName: string | null;
  createdAt: string;
  publishedAt: string | null;
  status: string;
  missing: string[];
  heroAssetId: string | null;
  viewersWithAccess: number;
  averageCompletion: number | null;
  folders: FolderRow[];
};

/** One file on a player's files tab. */
export type PlayerFile = {
  id: string;
  kind: string;
  title: string;
  fileName: string;
  tags: string[];
  sortOrder: number;
  visibility: string;
  state: string;
  failureReason: string | null;
  source: string;
  removedFromSource: boolean;
  durationSeconds: number | null;
  pageCount: number | null;
  isHero: boolean;
};

/** One player on the waiting list. */
export type WaitingRow = {
  id: string;
  name: string | null;
  folderName: string | null;
  videoCount: number;
  documentCount: number;
  missing: string[];
  waitingSince: string;
};

/** The waiting list's tiles. */
export type WaitingSummary = { waiting: number; noVolleybox: number; noPhoto: number; publishedToday: number };

/** One share link of the links list (the API's `ShareLinkListItemDto`). */
export type LinkRow = {
  id: string;
  code: string;
  type: string;
  recipientName: string;
  recipientOrganisation: string | null;
  playerNames: string[];
  status: string;
  access: string;
  expiresAt: string | null;
  createdAt: string;
};

/** One viewer on the viewers list. */
export type ViewerRow = {
  id: string;
  email: string;
  organisation: string | null;
  players: string[];
  playerCount: number;
  plays: number;
  watchSeconds: number;
  lastSeenAt: string | null;
  accessUntil: string | null;
  account: "SignedIn" | "LinkOnly" | "NotYet";
};

/** A viewer's profile. */
export type ViewerProfile = {
  id: string;
  email: string;
  organisation: string | null;
  firstSeenAt: string;
  lastSeenAt: string | null;
  account: string;
  plays: number;
  watchSeconds: number;
  playersAccessible: number;
  averageCompletion: number | null;
};

/** A player a viewer can open through one link. */
export type ViewerAccessRow = {
  playerId: string;
  playerName: string;
  linkId: string;
  code: string;
  status: string;
  grantedAt: string;
  expiresAt: string | null;
  plays: number;
  watchSeconds: number;
};

/** How a viewer watched one video. */
export type ViewerVideoRow = {
  playerId: string;
  playerName: string;
  assetId: string;
  title: string;
  plays: number;
  watchSeconds: number;
  uniqueSeconds: number;
  completion: number;
  lastWatchedAt: string;
};

/** A viewer's replay heatmap of one video. */
export type ViewerHeatmap = { assetId: string; title: string; durationSeconds: number; bucketSeconds: number; buckets: number[] };

/** One play in a viewer's session list. */
export type ViewerSessionRow = { watchId: string; playerName: string; videoTitle: string; startedAt: string; lastSeenAt: string; watchSeconds: number; completion: number };

/** A team member. */
export type UserRow = {
  id: string;
  displayName: string;
  username: string | null;
  email: string | null;
  signIn: "Email" | "Telegram";
  role: "Admin" | "Analyst";
  status: "Pending" | "Active" | "Declined" | "Removed";
  playerCount: number;
  lastActiveAt: string | null;
  requestedAt: string;
};

/** One audit-log entry. */
export type AuditRow = { id: string; at: string; actorId: string | null; actorName: string | null; action: string; targetType: string; targetId: string; data: string | null };

/** One file that is not ready yet. */
export type JobRow = {
  assetId: string;
  playerId: string;
  playerName: string;
  title: string;
  fileName: string;
  kind: string;
  state: string;
  failureReason: string | null;
  createdAt: string;
};

/** The jobs page's tiles. */
export type JobsSummary = {
  queued: number;
  copying: number;
  processing: number;
  failed: number;
  folders: number;
  lostFolders: number;
  lastDriveSyncAt: string | null;
  emailsSentLast24Hours: number;
  emailsFailed: number;
  emailsQueued: number;
};

/** One email on the queue (masked recipient). */
export type EmailRow = {
  id: string;
  kind: string;
  template: string;
  recipient: string;
  status: string;
  attempts: number;
  provider: string | null;
  lastError: string | null;
  queuedAt: string;
};

/** The console's settings. */
export type SettingsData = {
  contactEmail: string;
  digestAt: string;
  quietHoursStart: string;
  quietHoursEnd: string;
  defaultLinkExpiryDays: number;
  linkExpiryChoices: number[];
  timeZone: string;
  updatedAt: string;
};

/** The personal link an approved access request made. */
export type AccessRequestLink = { id: string; code: string; path: string; expiresAt: string | null };

/** One access request of the inbox (`GET /admin/access-requests`, P-4). */
export type AccessRequestRow = {
  id: string;
  player: { id: string; name: string | null; slug: string | null };
  email: string;
  about: string;
  source: "EmailCode" | "SignedIn" | string;
  status: "Pending" | "Approved" | "Declined" | string;
  createdAt: string;
  decidedAt: string | null;
  decidedBy: string | null;
  shareLink: AccessRequestLink | null;
};
