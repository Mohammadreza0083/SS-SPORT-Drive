/** UI copy of the Telegram Mini App (English; kept out of the components). */
export const miniAppCopy = {
  signingIn: "Signing you in…",
  greeting: (name: string) => `Hello, ${name}`,
  outsideTelegram: "This page is the SS-SPORT Drive Mini App. Open it from the SS-SPORT Drive bot in Telegram.",
  unauthorized: "Your Telegram sign-in could not be verified or has expired. Close and reopen the Mini App.",
  forbidden: "You do not have team access yet. Send /start and ask for access in the bot.",
  error: "Something went wrong. Please try again in a moment.",
  newPlayer: "New player",
  comingSoon: "My players arrives here soon.",
} as const;

/**
 * Writes a count with its noun in the singular or plural.
 *
 * @param count - The count.
 * @param noun - The singular noun.
 * @returns For example `1 video` or `14 videos`.
 */
function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

/** UI copy of the new-player form (T-4a). */
export const newPlayerCopy = {
  title: "New player",
  category: "Category",
  men: "Men",
  women: "Women",
  name: "Full name",
  nameHint: "Use Latin letters only, as on Volleybox.",
  nationality: "Nationality",
  nationalityPlaceholder: "Search a country",
  positions: "Positions",
  positionsHint: "First tap is the primary position, second tap the secondary.",
  volleybox: "Volleybox link",
  volleyboxNew: (id: number) => `Player ID ${id} · new player`,
  volleyboxExisting: (name: string) => `${name} already has a page. Updating players comes soon.`,
  drive: "Drive folder link",
  driveChecking: "Checking the folder…",
  driveFound: (name: string, videos: number, documents: number, scouts: number, size: string, truncated: boolean) =>
    `${name} · ${plural(videos, "video")} · ${plural(documents, "document")} · ${plural(scouts, "scout file")} · ${size}${truncated ? " (first 2000 files)" : ""}`,
  photoLater: "The photo is added in the next step.",
  create: "Create player",
  creating: "Creating…",
  created: (name: string) => `${name} is saved and waiting for an admin to publish. Add the photo next.`,
  rejected: "The player could not be saved. Check the fields and try again.",
} as const;

/** UI copy of the photo step (T-4b). */
export const photoCopy = {
  label: "Player photo",
  hint: "A clear photo of the player, at least 800 px on its shorter side. JPEG, PNG, WebP or HEIC, up to 15 MB.",
  uploading: "Uploading…",
  complete: (name: string) => `${name} is complete and waiting for an admin to publish.`,
  stillMissing: (details: string) => `Photo saved. Still missing: ${details}.`,
  replace: "Choose another photo",
  rejected: "The photo could not be saved. Try another one.",
} as const;

/** Messages for the API's rule codes, so the form explains what to fix. */
export const ruleMessages: Record<string, string> = {
  "player.name_required": "Enter the player's full name.",
  "player.name_too_long": "The name is too long (100 characters at most).",
  "player.name_invalid_characters": "Use Latin letters only, as on Volleybox.",
  "player.nationality_invalid": "Choose a country from the list.",
  "player.positions_required": "Choose at least one position.",
  "player.too_many_positions": "Choose at most two positions.",
  "player.duplicate_position": "Choose two different positions.",
  "player.volleybox_url_invalid": "Paste the player's Volleybox page, e.g. https://volleybox.net/name-p12345.",
  "player.volleybox_not_a_player": "This is a team page. Paste the player's Volleybox page.",
  "player.volleybox_taken": "A player with this Volleybox page already exists.",
  "drive_folder.link_invalid": "Paste the link of the player's Drive folder.",
  "drive_folder.taken": "This Drive folder already belongs to a player.",
  "drive_folder.no_access": "The folder cannot be found, or the SS-SPORT Drive account has no access to it.",
  "drive_folder.outside_shared_drive": "This folder is not in the SS-SPORT Shared Drive. Move it there, then paste its link again.",
  "drive_folder.not_a_folder": "This link opens a file. Paste the link of the player folder.",
  "drive.unavailable": "Google Drive cannot be reached right now. Try again in a moment.",
  "player.photo_too_small": "The photo must be at least 800 px on its shorter side.",
  "player.photo_too_large": "The photo is larger than 15 MB.",
  "player.photo_unreadable": "Send a JPEG, PNG, WebP or HEIC photo.",
};
