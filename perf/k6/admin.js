/**
 * k6 benchmark for the admin API behind the Admin policy (backlog O-10, with L-1, L-4 and L-6).
 *
 * Since O-10 every /admin/* endpoint is refused at the HTTP layer unless the caller is an authenticated admin, before
 * any handler or database work; the MediatR role check still runs as the second gate. The script signs in as an admin
 * with Telegram-signed Mini App initData (the same check as miniapp.js) or, with ADMIN_SESSION, with the console's
 * magic-link session in X-Admin-Session. Traffic shapes, side by side:
 *  - links_list:   GET  /admin/links?page=1&pageSize=20           → 200 (paged in SQL)
 *  - link_get:     GET  /admin/links/{id}                         → 200 (link + recipient + players)
 *  - link_viewing: GET  /admin/links/{id}/viewing?page=1&pageSize=20 → 200 (watch metrics, paged in SQL)
 *  - emails:       GET  /admin/emails?pageSize=20                 → 200 (cursor-paged queue, newest first)
 *  - anonymous:    GET  /admin/links, no credentials              → 401 (the Admin policy; no database work)
 *  - analyst:      GET  /admin/links as an analyst                → 403 (opt-in with ANALYST_TELEGRAM_ID; one user lookup)
 *  - no_expiry:    POST /admin/links without expiresInDays        → 400 (the validator; every link must expire)
 *  - create_link:  POST /admin/links, 7-day expiry                → 201 (opt-in with CREATE_LINKS=1 and PLAYER_ID; writes)
 *
 * Usage (see perf/k6/README.md):
 *   k6 run -e BASE_URL=http://localhost:5080 -e BOT_TOKEN=... -e ADMIN_TELEGRAM_ID=... -e PROFILE=smoke perf/k6/admin.js
 *
 * Environment:
 *   BASE_URL             API origin (default http://localhost:5080)
 *   BOT_TOKEN            must equal the API's Telegram__BotToken (required unless ADMIN_SESSION is set; never logged)
 *   ADMIN_TELEGRAM_ID    Telegram id of an active admin in the API's database (required unless ADMIN_SESSION is set)
 *   ADMIN_SESSION        a live admin console session token (X-Admin-Session) to use instead of initData (never logged)
 *   ANALYST_TELEGRAM_ID  Telegram id of an active analyst, for the 403 scenario (empty = skip it; needs BOT_TOKEN)
 *   PROFILE              smoke (default, 30 s, light) or load (~3.5 min, ramps to the peak rate)
 *   PEAK_RATE            admin list reads per second at the peak of the load profile (default 20)
 *   CREATE_LINKS         1 to include create_link (adds a link and possibly a recipient per iteration)
 *   PLAYER_ID            a published player for create_link and no_expiry (required with CREATE_LINKS=1; no_expiry
 *                        uses a random id when empty, which the validator refuses before the player is read)
 *
 * Setup reads the newest share link for link_get and link_viewing; with CREATE_LINKS=1 it creates one when none exists.
 * The API allows 300 requests per client per minute; raise RateLimiting__PermitsPerMinute for a load run (see README).
 */
import http from 'k6/http';
import crypto from 'k6/crypto';
import exec from 'k6/execution';
import { check, fail } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const BOT_TOKEN = __ENV.BOT_TOKEN || '';
const ADMIN_ID = Number(__ENV.ADMIN_TELEGRAM_ID || 0);
const ADMIN_SESSION = __ENV.ADMIN_SESSION || '';
const ANALYST_ID = Number(__ENV.ANALYST_TELEGRAM_ID || 0);
const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const PEAK_RATE = Number(__ENV.PEAK_RATE || 20);
const CREATE_LINKS = __ENV.CREATE_LINKS === '1';
const PLAYER_ID = __ENV.PLAYER_ID || '';
const ANALYST = ANALYST_ID > 0 && BOT_TOKEN !== '';

/**
 * Sent with every request, as the reverse proxy does: outside Development the API redirects plain HTTP to HTTPS
 * (O-9) unless a trusted proxy (loopback included) says the client used HTTPS.
 */
const PROXY = { 'X-Forwarded-Proto': 'https' };

/** The initData secret key, derived once from the bot token as Telegram does. */
const SECRET_KEY = BOT_TOKEN ? crypto.hmac('sha256', 'WebAppData', BOT_TOKEN, 'binary') : null;

/** Letters for unique recipient names; names allow letters only. */
const LETTERS = 'abcdefghijklmnopqrstuvwxyz';

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
    // A handful of admins browse the console: the link list most, a link's details and viewing next.
    links_list: scenario('linksList', 5, 1),
    link_get: scenario('linkGet', 3, 0.6),
    link_viewing: scenario('linkViewing', 2, 0.4),
    emails: scenario('emails', 2, 0.3),
    // The refusals an attacker would hammer: they must stay cheap.
    anonymous: scenario('anonymous', 3, 0.5),
    ...(ANALYST ? { analyst: scenario('analyst', 1, 0.2) } : {}),
    no_expiry: scenario('noExpiry', 1, 0.1),
    ...(CREATE_LINKS ? { create_link: scenario('createLink', 1, 0.05) } : {}),
  },
  thresholds: {
    // Every response has the status its scenario expects (200/201, or 400/401/403 where that is the point).
    checks: ['rate>0.99'],
    // 400, 401 and 403 are expected in their scenarios and marked as such, so this counts real failures only.
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:links_list}': ['p(95)<200', 'p(99)<400'],
    'http_req_duration{scenario:link_get}': ['p(95)<150', 'p(99)<300'],
    'http_req_duration{scenario:link_viewing}': ['p(95)<250', 'p(99)<500'],
    'http_req_duration{scenario:emails}': ['p(95)<150', 'p(99)<300'],
    // Refused by the Admin policy before any handler: no database work at all.
    'http_req_duration{scenario:anonymous}': ['p(95)<50'],
    ...(ANALYST ? { 'http_req_duration{scenario:analyst}': ['p(95)<150'] } : {}),
    'http_req_duration{scenario:no_expiry}': ['p(95)<150'],
    ...(CREATE_LINKS ? { 'http_req_duration{scenario:create_link}': ['p(95)<400', 'p(99)<800'] } : {}),
  },
};

/**
 * Builds Telegram Mini App initData for a user, signed with the bot token exactly as Telegram signs it.
 * @param {number} telegramId the user.
 * @returns {string} the URL-encoded initData.
 */
function initData(telegramId) {
  const fields = {
    query_id: `bench${telegramId}`,
    user: JSON.stringify({ id: telegramId, first_name: 'Bench', username: `bench_${telegramId}` }),
    auth_date: `${Math.floor(Date.now() / 1000)}`,
  };
  const dataCheckString = Object.keys(fields).sort().map((key) => `${key}=${fields[key]}`).join('\n');
  fields.hash = crypto.hmac('sha256', SECRET_KEY, dataCheckString, 'hex');
  return Object.keys(fields).map((name) => `${name}=${encodeURIComponent(fields[name])}`).join('&');
}

/**
 * Request params for an admin call.
 * @param {string} name the request's tag, so URLs with ids group together.
 * @param {number[]} [expected] the statuses this request may answer.
 * @returns {object} the k6 params.
 */
function admin(name, expected = [200]) {
  const auth = ADMIN_SESSION ? { 'X-Admin-Session': ADMIN_SESSION } : { Authorization: `tma ${initData(ADMIN_ID)}` };
  return {
    headers: { ...PROXY, ...auth, 'Content-Type': 'application/json' },
    tags: { name },
    responseCallback: http.expectedStatuses(...expected),
  };
}

/**
 * Spells a number with letters only (base 26), so every created recipient gets a distinct, valid name.
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
 * Checks the settings and finds the newest share link for the per-link scenarios.
 * @returns {{ linkId: string }} the link to read.
 */
export function setup() {
  if (!ADMIN_SESSION && (!BOT_TOKEN || ADMIN_ID <= 0)) {
    fail('Set BOT_TOKEN and ADMIN_TELEGRAM_ID (an active admin), or ADMIN_SESSION.');
  }

  if (CREATE_LINKS && !PLAYER_ID) {
    fail('Set PLAYER_ID to a published player for CREATE_LINKS=1.');
  }

  const list = http.get(`${BASE_URL}/admin/links?page=1&pageSize=1`, admin('setup'));
  if (list.status !== 200) {
    fail(`GET /admin/links answered ${list.status}; is the caller an active admin and the API up?`);
  }

  const items = list.json('items');
  if (items.length > 0) {
    return { linkId: items[0].id };
  }

  if (!CREATE_LINKS) {
    fail('No share link exists yet; run once with CREATE_LINKS=1 and PLAYER_ID (see README).');
  }

  const body = JSON.stringify({ recipient: { name: 'Bench Setup', emails: ['bench-setup@example.com'] }, playerIds: [PLAYER_ID], expiresInDays: 90 });
  const created = http.post(`${BASE_URL}/admin/links`, body, admin('setup', [201]));
  if (created.status !== 201) {
    fail(`POST /admin/links answered ${created.status}; is PLAYER_ID a published player?`);
  }

  return { linkId: created.json('id') };
}

/** Reads the first page of links. */
export function linksList() {
  const response = http.get(`${BASE_URL}/admin/links?page=1&pageSize=20`, admin('links_list'));
  check(response, { 'links: 200': (r) => r.status === 200 });
}

/**
 * Reads one link.
 * @param {{ linkId: string }} data the setup data.
 */
export function linkGet(data) {
  const response = http.get(`${BASE_URL}/admin/links/${data.linkId}`, admin('link_get'));
  check(response, { 'link: 200': (r) => r.status === 200 });
}

/**
 * Reads a link's viewing metrics.
 * @param {{ linkId: string }} data the setup data.
 */
export function linkViewing(data) {
  const response = http.get(`${BASE_URL}/admin/links/${data.linkId}/viewing?page=1&pageSize=20`, admin('link_viewing'));
  check(response, { 'viewing: 200': (r) => r.status === 200 });
}

/** Reads the newest page of the email queue. */
export function emails() {
  const response = http.get(`${BASE_URL}/admin/emails?pageSize=20`, admin('emails'));
  check(response, { 'emails: 200': (r) => r.status === 200 });
}

/** Calls an admin endpoint with no credentials: the Admin policy answers 401. */
export function anonymous() {
  const response = http.get(`${BASE_URL}/admin/links`, { headers: PROXY, tags: { name: 'anonymous' }, responseCallback: http.expectedStatuses(401) });
  check(response, {
    'anonymous: 401': (r) => r.status === 401,
    'anonymous: problem details': (r) => (r.headers['Content-Type'] || '').startsWith('application/problem+json'),
  });
}

/** Calls an admin endpoint as an analyst: the Admin policy answers 403. */
export function analyst() {
  const params = {
    headers: { ...PROXY, Authorization: `tma ${initData(ANALYST_ID)}` },
    tags: { name: 'analyst' },
    responseCallback: http.expectedStatuses(403),
  };
  const response = http.get(`${BASE_URL}/admin/links`, params);
  check(response, { 'analyst: 403': (r) => r.status === 403 });
}

/** Asks for a link without an expiry: the validator refuses it with 400 (every link must expire). */
export function noExpiry() {
  const body = JSON.stringify({
    recipient: { name: 'Bench Recipient', emails: ['bench@example.com'] },
    playerIds: [PLAYER_ID || '0199f000-0000-7000-8000-000000000001'],
  });
  const response = http.post(`${BASE_URL}/admin/links`, body, admin('no_expiry', [400]));
  check(response, {
    'no expiry: 400': (r) => r.status === 400,
    'no expiry: names the field': (r) => r.status === 400 && r.json('errors.expiresInDays') !== undefined,
  });
}

/** Creates a 7-day link to PLAYER_ID for a new recipient. */
export function createLink() {
  const n = exec.scenario.iterationInTest;
  const body = JSON.stringify({
    recipient: { name: `Bench ${letters(n)}`, emails: [`bench${n}@example.com`] },
    playerIds: [PLAYER_ID],
    expiresInDays: 7,
  });
  const response = http.post(`${BASE_URL}/admin/links`, body, admin('create_link', [201]));
  check(response, { 'create: 201': (r) => r.status === 201 });
}
