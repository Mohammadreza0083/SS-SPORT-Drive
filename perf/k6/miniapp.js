/**
 * k6 benchmark for the Telegram Mini App API (backlog T-3, T-4a, T-5, T-7a, T-7b).
 *
 * Every request signs in with Telegram-signed initData (`Authorization: tma <initData>`), built here exactly as
 * Telegram builds it, so the API does its full HMAC check and user lookup on each call. Six traffic shapes run side by side:
 *  - profile:        GET  /miniapp/me                      → 200 (signature + user lookup + profile read)
 *  - countries:      GET  /miniapp/countries               → 200 (245 countries from memory)
 *  - volleybox:      GET  /miniapp/volleybox?url=…         → 200 (parse + one indexed lookup)
 *  - create_player:  POST /miniapp/players                 → 201 (opt-in with CREATE_PLAYERS=1; see below)
 *  - forged:         GET  /miniapp/me with a bad signature → 401 (no database work)
 *  - not_member:     GET  /miniapp/me as a stranger        → 403 (signature + one lookup)
 *  - photo_upload:   PUT  /miniapp/players/{id}/photo      → 200 (opt-in with SEEDED_PLAYERS; JPEG → WebP + 2 objects stored)
 *  - photo_read:     GET  /miniapp/players/{id}/photo      → 200 (opt-in with SEEDED_PLAYERS; streams the WebP)
 *  - player_card:    GET  /miniapp/players/{id}            → 200 (opt-in with SEEDED_PLAYERS; update-mode card, T-5)
 *  - player_edit:    PATCH /miniapp/players/{id}           → 200 (opt-in with SEEDED_PLAYERS; nationality + positions, T-5)
 *  - drive_check:    GET  /miniapp/drive-folder?url=…      → 200 (opt-in with DRIVE_FOLDER_ID; reads real Google Drive)
 *  - my_players:     GET  /miniapp/players?page=…          → 200 (opt-in with SEEDED_PLAYERS; My players, T-7a)
 *  - my_players_filter: GET /miniapp/players?filter=…&search=… → 200 (opt-in; the chips and the search, T-7a)
 *  - player_files:   GET  /miniapp/players/{id}/files      → 200 (opt-in with SEEDED_PLAYERS; files in page order, T-7a)
 *  - file_edit:      PATCH /miniapp/players/{id}/files/{fileId} → 200 (opt-in; title + tags from the sheet, T-7b)
 *
 * Usage (see perf/k6/README.md):
 *   k6 run -e BASE_URL=http://localhost:5080 -e BOT_TOKEN=... -e ANALYST_TELEGRAM_ID=... -e PROFILE=smoke perf/k6/miniapp.js
 *
 * Environment:
 *   BASE_URL             API origin (default http://localhost:5080)
 *   BOT_TOKEN            must equal the API's Telegram__BotToken (required; never logged)
 *   ANALYST_TELEGRAM_ID  Telegram id of an active analyst or admin in the API's database (required)
 *   PROFILE              smoke (default, ~30 s, light) or load (~4 min, ramps to the peak rate)
 *   PEAK_RATE            profile reads per second at the peak of the load profile (default 50)
 *   CREATE_PLAYERS       1 to include create_player. Since T-4c every new player's Drive folder is read from Google
 *                        Drive, so this only works against an API whose Drive reader accepts the generated folder ids
 *                        (a test double); against real Drive the made-up folders are refused with 422.
 *   SEEDED_PLAYERS       how many seeded players the photo, update-mode and My players scenarios use (0 = skip them;
 *                        the older name PHOTO_PLAYERS still works). Their ids must follow
 *                        0199f000-0000-7000-8000-000000000001 … and they must belong to ANALYST_TELEGRAM_ID (see README).
 *                        The My players scenarios also need FILES_PER_PLAYER seeded files per player, with ids
 *                        0199f100-0000-7000-8000-{player × 10 + file} (see README).
 *   DRIVE_FOLDER_ID      a real, unlinked player folder in the Shared Drive (empty = skip drive_check). Every check
 *                        calls Google, so this scenario runs at a fixed, low rate (1/s smoke, 2/s load) to respect
 *                        the Drive API quota.
 */
import http from 'k6/http';
import crypto from 'k6/crypto';
import exec from 'k6/execution';
import { check, fail } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
/**
 * Sent with every request, as the reverse proxy does: outside Development the API redirects plain HTTP to HTTPS
 * (O-9) unless a trusted proxy (loopback included) says the client used HTTPS.
 */
const PROXY_HEADERS = { 'X-Forwarded-Proto': 'https' };
const BOT_TOKEN = __ENV.BOT_TOKEN || '';
const ANALYST_ID = Number(__ENV.ANALYST_TELEGRAM_ID || 0);
const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const PEAK_RATE = Number(__ENV.PEAK_RATE || 50);
const CREATE_PLAYERS = __ENV.CREATE_PLAYERS === '1';
const PHOTO_PLAYERS = Number(__ENV.SEEDED_PLAYERS || __ENV.PHOTO_PLAYERS || 0);
const DRIVE_FOLDER_ID = __ENV.DRIVE_FOLDER_ID || '';

/** How many files the README's seed SQL gives each seeded player (ids 0199f100-…-{player × 10 + file}). */
const FILES_PER_PLAYER = 5;

/** The filter chips of My players, used in turn by the filter scenario. */
const FILTERS = ['all', 'live', 'processing', 'needsAttention'];

/** How many players get a photo in setup, so the read scenario always finds one. */
const READ_PLAYERS = Math.min(PHOTO_PLAYERS, 20);

/** A photo-like 1200×1600 JPEG of 625 KB, read once in the init context. */
const PHOTO = PHOTO_PLAYERS > 0 ? open('./fixtures/player-1200x1600.jpg', 'b') : null;

/** A Telegram id nobody in the team has (the 403 scenario). */
const STRANGER_ID = 999000001;

/** Letters for the names of created players; names allow Latin letters only. */
const LETTERS = 'abcdefghijklmnopqrstuvwxyz';

/**
 * Builds a scenario that ignores the peak: a fixed rate for the whole run (for calls that reach Google).
 * @param {string} execName the exported function the scenario runs.
 * @param {number} rate requests per second.
 * @returns {object} the k6 scenario.
 */
function fixedRate(execName, rate) {
  return {
    executor: 'constant-arrival-rate',
    exec: execName,
    rate,
    timeUnit: '1s',
    duration: PROFILE === 'load' ? '3m30s' : '30s',
    preAllocatedVUs: 2,
    maxVUs: 20,
  };
}

/**
 * Builds one scenario: a constant rate for smoke, or a ramp to its share of the peak for load.
 * @param {string} execName the exported function the scenario runs.
 * @param {number} smokeRate requests per second in the smoke profile.
 * @param {number} share the scenario's share of PEAK_RATE in the load profile.
 * @returns {object} the k6 scenario.
 */
function scenario(execName, smokeRate, share) {
  if (PROFILE === 'load') {
    const peak = Math.max(1, Math.round(PEAK_RATE * share));
    return {
      executor: 'ramping-arrival-rate',
      exec: execName,
      startRate: 1,
      timeUnit: '1s',
      preAllocatedVUs: Math.max(2, Math.ceil(peak / 5)),
      maxVUs: Math.max(10, peak * 4),
      stages: [
        { target: peak, duration: '1m' },
        { target: peak, duration: '2m' },
        { target: 0, duration: '30s' },
      ],
    };
  }

  return { executor: 'constant-arrival-rate', exec: execName, rate: smokeRate, timeUnit: '1s', duration: '30s', preAllocatedVUs: 2, maxVUs: 20 };
}

export const options = {
  scenarios: {
    // Opening the Mini App and moving around it reads the profile most; the form calls are rarer.
    profile: scenario('profile', 10, 1),
    countries: scenario('countries', 3, 0.2),
    volleybox: scenario('volleybox', 5, 0.4),
    ...(CREATE_PLAYERS ? { create_player: scenario('createPlayer', 2, 0.1) } : {}),
    forged: scenario('forged', 2, 0.2),
    not_member: scenario('notMember', 1, 0.1),
    ...(PHOTO_PLAYERS > 0
      ? {
        photo_upload: scenario('photoUpload', 1, 0.05),
        photo_read: scenario('photoRead', 3, 0.3),
        player_card: scenario('playerCard', 5, 0.4),
        player_edit: scenario('playerEdit', 2, 0.1),
        // Opening My players and paging is the commonest; editing a file in the sheet is rare.
        my_players: scenario('myPlayers', 5, 0.4),
        my_players_filter: scenario('myPlayersFilter', 3, 0.2),
        player_files: scenario('playerFiles', 5, 0.4),
        file_edit: scenario('fileEdit', 2, 0.1),
      }
      : {}),
    ...(DRIVE_FOLDER_ID ? { drive_check: fixedRate('driveCheck', PROFILE === 'load' ? 2 : 1) } : {}),
  },
  thresholds: {
    // Every response has the status its scenario expects (200/201, or 401/403 where that is the point).
    checks: ['rate>0.99'],
    // 401 and 403 are expected in their scenarios and marked as such, so this counts real failures only.
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:profile}': ['p(95)<150', 'p(99)<300'],
    'http_req_duration{scenario:countries}': ['p(95)<150'],
    'http_req_duration{scenario:volleybox}': ['p(95)<150'],
    ...(CREATE_PLAYERS ? { 'http_req_duration{scenario:create_player}': ['p(95)<400', 'p(99)<800'] } : {}),
    'http_req_duration{scenario:forged}': ['p(95)<100'],
    'http_req_duration{scenario:not_member}': ['p(95)<150'],
    // The analyst waits for these after picking a photo or pasting a link, so seconds, not milliseconds.
    ...(PHOTO_PLAYERS > 0
      ? {
        'http_req_duration{scenario:photo_upload}': ['p(95)<2000'],
        'http_req_duration{scenario:photo_read}': ['p(95)<150'],
        // The card reads the player, its owner and two folder queries; an edit reads, checks and saves one player.
        'http_req_duration{scenario:player_card}': ['p(95)<150', 'p(99)<300'],
        'http_req_duration{scenario:player_edit}': ['p(95)<300', 'p(99)<600'],
        // One SQL query per page: players with correlated counts of their files and folders, then the total.
        'http_req_duration{scenario:my_players}': ['p(95)<150', 'p(99)<300'],
        'http_req_duration{scenario:my_players_filter}': ['p(95)<200', 'p(99)<400'],
        'http_req_duration{scenario:player_files}': ['p(95)<150', 'p(99)<300'],
        // Reads the player and the file, checks who may edit, saves the file.
        'http_req_duration{scenario:file_edit}': ['p(95)<300', 'p(99)<600'],
      }
      : {}),
    ...(DRIVE_FOLDER_ID ? { 'http_req_duration{scenario:drive_check}': ['p(95)<3000'] } : {}),
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

/** The secret key Telegram derives from the bot token: HMAC-SHA256("WebAppData", botToken). */
const SECRET_KEY = BOT_TOKEN ? crypto.hmac('sha256', 'WebAppData', BOT_TOKEN, 'binary') : null;

/**
 * Builds initData for a Telegram user, signed the way Telegram signs it, issued now.
 * @param {number} telegramId the user's Telegram id.
 * @param {string} [token] sign as if for this bot token instead (the forged scenario).
 * @returns {string} the URL-encoded initData.
 */
function initData(telegramId, token) {
  const fields = {
    query_id: `bench${telegramId}`,
    user: JSON.stringify({ id: telegramId, first_name: 'Bench', username: `bench_${telegramId}` }),
    auth_date: `${Math.floor(Date.now() / 1000)}`,
  };
  const dataCheckString = Object.keys(fields).sort().map((key) => `${key}=${fields[key]}`).join('\n');
  const key = token ? crypto.hmac('sha256', 'WebAppData', token, 'binary') : SECRET_KEY;
  fields.hash = crypto.hmac('sha256', key, dataCheckString, 'hex');
  return Object.keys(fields).map((name) => `${name}=${encodeURIComponent(fields[name])}`).join('&');
}

/**
 * Builds the request params with the Mini App Authorization header.
 * @param {string} data the initData.
 * @param {object} [extra] extra request params.
 * @returns {object} the k6 params.
 */
function signed(data, extra = {}) {
  return { headers: { ...PROXY_HEADERS, Authorization: `tma ${data}`, 'Content-Type': 'application/json' }, ...extra };
}

/**
 * Spells a number with letters only (base 26), so every created player gets a distinct, valid name.
 * @param {number} value a non-negative integer.
 * @returns {string} the letters.
 */
function letters(value) {
  let text = '';
  let rest = value;
  do {
    text = LETTERS[rest % 26] + text;
    rest = Math.floor(rest / 26);
  } while (rest > 0);
  return text;
}

/**
 * Checks the settings and that the analyst can sign in, and picks an id window unique to this run.
 * @returns {{ base: number }} shared test data.
 */
export function setup() {
  if (!BOT_TOKEN) {
    fail("Set BOT_TOKEN to the API's Telegram__BotToken.");
  }

  if (!ANALYST_ID) {
    fail('Set ANALYST_TELEGRAM_ID to the Telegram id of an active analyst or admin.');
  }

  const response = http.get(`${BASE_URL}/miniapp/me`, signed(initData(ANALYST_ID)));
  if (response.status !== 200) {
    fail(`Sign-in failed with ${response.status}: is the API up, the bot token right and the analyst active?`);
  }

  for (let n = 1; n <= READ_PLAYERS; n += 1) {
    const uploaded = putPhoto(n);
    if (uploaded.status !== 200) {
      fail(`Seeding a photo failed with ${uploaded.status}: are the ${PHOTO_PLAYERS} players seeded for the analyst and is storage up?`);
    }
  }

  // Volleybox ids have at most 9 digits (the domain's rule): each minute of the clock gets its own window of 100 000
  // ids between 100 000 000 and 999 999 999. Runs started in the same minute share a window, so wait a minute between
  // runs. (The windows used to run up to 1.9 billion, so in about half of all minutes every id was refused.)
  return { base: 100000000 + (Math.floor(Date.now() / 60000) % 8999) * 100000 };
}

/** profile: the signed-in analyst reads their profile. */
export function profile() {
  const response = http.get(`${BASE_URL}/miniapp/me`, signed(initData(ANALYST_ID)));
  check(response, { 'profile: 200': (r) => r.status === 200 });
}

/** countries: the nationality picker loads the catalogue. */
export function countries() {
  const response = http.get(`${BASE_URL}/miniapp/countries`, signed(initData(ANALYST_ID)));
  check(response, { 'countries: 200 with 245': (r) => r.status === 200 && r.json().length === 245 });
}

/**
 * volleybox: a pasted Volleybox link is checked live.
 * @param {{ base: number }} data shared test data.
 */
export function volleybox(data) {
  const id = data.base + 50000 + (exec.scenario.iterationInTest % 50000);
  const url = encodeURIComponent(`https://volleybox.net/bench-player-p${id}`);
  const response = http.get(`${BASE_URL}/miniapp/volleybox?url=${url}`, signed(initData(ANALYST_ID)));
  check(response, { 'volleybox: 200': (r) => r.status === 200 && r.json().volleyboxId === id });
}

/**
 * create_player: the form is sent; every player gets its own name, Volleybox id and Drive folder.
 * @param {{ base: number }} data shared test data.
 */
export function createPlayer(data) {
  const n = exec.scenario.iterationInTest;
  const volleyboxId = data.base + n;
  const body = JSON.stringify({
    category: n % 2 === 0 ? 'Men' : 'Women',
    name: `Bench ${letters(volleyboxId)}`,
    nationalityCode: 'IR',
    positions: n % 3 === 0 ? ['OH', 'OPP'] : ['MB'],
    volleyboxUrl: `https://volleybox.net/bench-p${volleyboxId}`,
    driveFolderUrl: `https://drive.google.com/drive/folders/k6bench_${volleyboxId}`,
  });
  const response = http.post(`${BASE_URL}/miniapp/players`, body, signed(initData(ANALYST_ID)));
  check(response, { 'create player: 201': (r) => r.status === 201 });
}

/** forged: initData signed for another bot, answered 401 (expected, so not a failed request). */
export function forged() {
  const response = http.get(`${BASE_URL}/miniapp/me`, signed(initData(ANALYST_ID, '999:not-our-bot'), { responseCallback: http.expectedStatuses(401) }));
  check(response, { 'forged: 401': (r) => r.status === 401 });
}

/** not_member: a real Telegram user who is not in the team, answered 403 (expected). */
export function notMember() {
  const response = http.get(`${BASE_URL}/miniapp/me`, signed(initData(STRANGER_ID), { responseCallback: http.expectedStatuses(403) }));
  check(response, { 'not member: 403': (r) => r.status === 403 });
}

/**
 * The id of the n-th seeded player (1-based), as the README's seed SQL creates them.
 * @param {number} n the player number.
 * @returns {string} the player id.
 */
function playerId(n) {
  return `0199f000-0000-7000-8000-${String(n).padStart(12, '0')}`;
}

/**
 * Uploads the test photo for the n-th seeded player.
 * @param {number} n the player number.
 * @returns {object} the k6 response.
 */
function putPhoto(n) {
  return http.put(`${BASE_URL}/miniapp/players/${playerId(n)}/photo`, PHOTO, {
    headers: { ...PROXY_HEADERS, Authorization: `tma ${initData(ANALYST_ID)}`, 'Content-Type': 'image/jpeg' },
  });
}

/** photo_upload: the analyst uploads a photo; it is checked, converted to WebP and stored with the original. */
export function photoUpload() {
  const response = putPhoto(1 + (exec.scenario.iterationInTest % PHOTO_PLAYERS));
  check(response, { 'photo upload: 200': (r) => r.status === 200 });
}

/** photo_read: the form shows the stored photo. */
export function photoRead() {
  const n = 1 + (exec.scenario.iterationInTest % READ_PLAYERS);
  const response = http.get(`${BASE_URL}/miniapp/players/${playerId(n)}/photo`, signed(initData(ANALYST_ID)));
  check(response, { 'photo read: 200 webp': (r) => r.status === 200 && r.headers['Content-Type'] === 'image/webp' });
}

/** drive_check: a pasted folder link is checked against real Google Drive. */
export function driveCheck() {
  const response = http.get(`${BASE_URL}/miniapp/drive-folder?url=${encodeURIComponent(DRIVE_FOLDER_ID)}`, signed(initData(ANALYST_ID)));
  check(response, { 'drive check: 200': (r) => r.status === 200 });
}

/** player_card: update mode opens an existing player (T-5). */
export function playerCard() {
  const n = 1 + (exec.scenario.iterationInTest % PHOTO_PLAYERS);
  const response = http.get(`${BASE_URL}/miniapp/players/${playerId(n)}`, signed(initData(ANALYST_ID)));
  check(response, { 'player card: 200 editable': (r) => r.status === 200 && r.json().canEditDetails === true });
}

/** player_edit: the owner changes the nationality and positions of a waiting player (T-5). */
export function playerEdit() {
  const i = exec.scenario.iterationInTest;
  const body = JSON.stringify({ nationalityCode: i % 2 === 0 ? 'IT' : 'IR', positions: i % 3 === 0 ? ['OH', 'OPP'] : ['MB'] });
  const response = http.patch(`${BASE_URL}/miniapp/players/${playerId(1 + (i % PHOTO_PLAYERS))}`, body, signed(initData(ANALYST_ID)));
  check(response, { 'player edit: 200': (r) => r.status === 200 });
}

/**
 * The id of a seeded file, as the README's seed SQL creates them.
 * @param {number} player the player number (1-based).
 * @param {number} file the file number (1 to FILES_PER_PLAYER).
 * @returns {string} the file id.
 */
function fileId(player, file) {
  return `0199f100-0000-7000-8000-${String(player * 10 + file).padStart(12, '0')}`;
}

/** my_players: the analyst opens My players and pages through it (T-7a). */
export function myPlayers() {
  const pages = Math.max(1, Math.ceil(PHOTO_PLAYERS / 20));
  const page = 1 + (exec.scenario.iterationInTest % pages);
  const response = http.get(`${BASE_URL}/miniapp/players?page=${page}`, signed(initData(ANALYST_ID)));
  check(response, { 'my players: 200 with a page': (r) => r.status === 200 && r.json().items.length > 0 && r.json().page === page });
}

/** my_players_filter: the analyst taps a filter chip, and every other time also searches (T-7a). */
export function myPlayersFilter() {
  const i = exec.scenario.iterationInTest;
  const search = i % 2 === 0 ? '&search=bench' : '';
  const response = http.get(`${BASE_URL}/miniapp/players?filter=${FILTERS[i % FILTERS.length]}${search}`, signed(initData(ANALYST_ID)));
  check(response, { 'my players filter: 200': (r) => r.status === 200 && Array.isArray(r.json().items) });
}

/** player_files: the player detail lists the files in page order (T-7a). */
export function playerFiles() {
  const n = 1 + (exec.scenario.iterationInTest % PHOTO_PLAYERS);
  const response = http.get(`${BASE_URL}/miniapp/players/${playerId(n)}/files`, signed(initData(ANALYST_ID)));
  check(response, { 'player files: 200 with the files': (r) => r.status === 200 && r.json().totalCount === FILES_PER_PLAYER });
}

/**
 * file_edit: the owner saves the edit-video sheet with a new title and tags (T-7b). It spreads over every seeded file,
 * so two edits rarely meet on one file; when they do, the second is a 409 (D-13), which counts as a failure here.
 */
export function fileEdit() {
  const i = exec.scenario.iterationInTest;
  const player = 1 + (i % PHOTO_PLAYERS);
  const file = 1 + (Math.floor(i / PHOTO_PLAYERS) % FILES_PER_PLAYER);
  const body = JSON.stringify({ title: `Bench clip ${i}`, tags: i % 2 === 0 ? ['Highlight', 'Spike'] : ['FullMatch'] });
  const response = http.patch(`${BASE_URL}/miniapp/players/${playerId(player)}/files/${fileId(player, file)}`, body, signed(initData(ANALYST_ID)));
  check(response, { 'file edit: 200': (r) => r.status === 200 && r.json().title === `Bench clip ${i}` });
}
