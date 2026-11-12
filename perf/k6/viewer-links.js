/**
 * k6 benchmark for personal links: the email gate (backlog L-2) and what a signed-in viewer reads (V-2b).
 *
 * It calls the API directly, the way the web app does on the viewer's behalf (tokens in headers, never cookies).
 * Gate scenarios (need LINK_CODE):
 *  - gate_card:     GET  /links/{code}                        → 200 (the gate card: link + recipient + player)
 *  - unknown_link:  GET  /links/{unknown}                     → 404 (the same answer for every code that does not open)
 *  - wrong_code:    POST /links/{code}/verify, wrong code     → 422 (salted-hash check of the latest challenge)
 *  - stale_device:  POST /links/{code}/session, bad device    → 401 (one hashed lookup)
 *  - email_code:    POST /links/{code}/email-code             → 202 (opt-in with SEND_CODES=1: queues a real email
 *                   job per request, each to a fresh forward address; use only with a pickup directory)
 * Viewer scenarios (need SESSION_TOKEN for LINK_CODE):
 *  - session:       GET  /links/{code}/session                → 200
 *  - content:       GET  /links/{code}/content                → 200 (link + session + recipient)
 *  - players:       GET  /links/{code}/players                → 200 (the roster: one projection for the page)
 *  - player:        GET  /links/{code}/players/{id}           → 200 (header + stats)
 *  - videos:        GET  /links/{code}/players/{id}/videos    → 200 (paged in SQL, page size 100)
 *  - documents:     GET  /links/{code}/players/{id}/documents → 200 (paged in SQL, page size 100)
 *  - photo:         GET  /links/{code}/players/{id}/photo     → 200 (streams the WebP; 404 counted as expected when
 *                   a player has none)
 *  - no_session:    GET  /links/{code}/content, no token      → 401
 *
 * Opening a player page in the browser makes the web app call content, player, videos and documents (and the photo);
 * a roster page calls content, players and one photo per card.
 *
 * Usage (see perf/k6/README.md):
 *   k6 run -e BASE_URL=http://localhost:5080 -e LINK_CODE=Ab3xYz -e SESSION_TOKEN=... -e PROFILE=smoke perf/k6/viewer-links.js
 *
 * Environment:
 *   BASE_URL       API origin (default http://localhost:5080)
 *   LINK_CODE      an open personal link (required)
 *   RECIPIENT      one of the link's recipient emails, for the wrong-code scenario (required)
 *   SESSION_TOKEN  a live viewer session for LINK_CODE (empty = skip the viewer scenarios; never logged)
 *   PROFILE        smoke (default, 30 s, light) or load (~3.5 min, ramps to the peak rate)
 *   PEAK_RATE      player-page reads per second at the peak of the load profile (default 50)
 *   SEND_CODES     1 to include email_code
 *
 * The API's per-address budgets (30/min for the gate, 240/min for content) would stop one k6 machine within seconds,
 * so run the benchmark against an API started with raised RateLimiting__* values (see README).
 */
import http from 'k6/http';
import exec from 'k6/execution';
import { check, fail } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const LINK_CODE = __ENV.LINK_CODE || '';
const RECIPIENT = __ENV.RECIPIENT || '';
const SESSION_TOKEN = __ENV.SESSION_TOKEN || '';
const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const PEAK_RATE = Number(__ENV.PEAK_RATE || 50);
const SEND_CODES = __ENV.SEND_CODES === '1';
const VIEWER = SESSION_TOKEN !== '';

/**
 * Every request says it came through the TLS proxy, as in production: outside Development the API redirects plain
 * HTTP to HTTPS. Start the API with Security__TrustedProxies covering the k6 machine (see README).
 */
const PROXY = { 'X-Forwarded-Proto': 'https' };

/** A well-formed code that no link has: the 404 path. */
const UNKNOWN_CODE = 'Zz9Yy8';

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
    gate_card: scenario('gateCard', 5, 0.3),
    unknown_link: scenario('unknownLink', 2, 0.1),
    wrong_code: scenario('wrongCode', 2, 0.1),
    stale_device: scenario('staleDevice', 2, 0.1),
    ...(SEND_CODES ? { email_code: scenario('emailCode', 1, 0.05) } : {}),
    ...(VIEWER
      ? {
        // Every page view reads the overview and one player; a roster view also lists the players.
        session: scenario('session', 3, 0.2),
        content: scenario('content', 10, 1),
        players: scenario('players', 5, 0.5),
        player: scenario('player', 10, 1),
        videos: scenario('videos', 10, 1),
        documents: scenario('documents', 10, 1),
        photo: scenario('photo', 10, 1),
        no_session: scenario('noSession', 2, 0.1),
      }
      : {}),
  },
  thresholds: {
    // Every response has the status its scenario expects (401/404/422 where that is the point).
    checks: ['rate>0.99'],
    // Expected 401/404/422 are marked as such per request, so this counts real failures only.
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:gate_card}': ['p(95)<150', 'p(99)<300'],
    'http_req_duration{scenario:unknown_link}': ['p(95)<100'],
    'http_req_duration{scenario:wrong_code}': ['p(95)<200'],
    'http_req_duration{scenario:stale_device}': ['p(95)<150'],
    ...(SEND_CODES ? { 'http_req_duration{scenario:email_code}': ['p(95)<300'] } : {}),
    ...(VIEWER
      ? {
        'http_req_duration{scenario:session}': ['p(95)<150'],
        'http_req_duration{scenario:content}': ['p(95)<150', 'p(99)<300'],
        'http_req_duration{scenario:players}': ['p(95)<200', 'p(99)<400'],
        'http_req_duration{scenario:player}': ['p(95)<200', 'p(99)<400'],
        'http_req_duration{scenario:videos}': ['p(95)<200', 'p(99)<400'],
        'http_req_duration{scenario:documents}': ['p(95)<200', 'p(99)<400'],
        'http_req_duration{scenario:photo}': ['p(95)<250'],
        'http_req_duration{scenario:no_session}': ['p(95)<100'],
      }
      : {}),
  },
};

/**
 * Request params for a viewer read.
 * @param {string} name the request's tag, so URLs with ids group together.
 * @param {number[]} [expected] the statuses this request may answer.
 * @returns {object} the k6 params.
 */
function viewer(name, expected = [200]) {
  return { headers: { ...PROXY, 'X-Viewer-Session': SESSION_TOKEN }, tags: { name }, responseCallback: http.expectedStatuses(...expected) };
}

/**
 * Checks the settings and, for the viewer scenarios, reads the link's players once.
 * @returns {{ players: string[] }} the player ids to read.
 */
export function setup() {
  if (!LINK_CODE) {
    fail('Set LINK_CODE to an open personal link.');
  }

  if (!RECIPIENT) {
    fail("Set RECIPIENT to one of the link's recipient emails.");
  }

  const card = http.get(`${BASE_URL}/links/${LINK_CODE}`, { headers: PROXY });
  if (card.status !== 200) {
    fail(`GET /links/${LINK_CODE} answered ${card.status}; is the link open and the API up?`);
  }

  if (!VIEWER) {
    return { players: [] };
  }

  const list = http.get(`${BASE_URL}/links/${LINK_CODE}/players?page=1&pageSize=20`, viewer('setup'));
  if (list.status !== 200) {
    fail(`GET /links/${LINK_CODE}/players answered ${list.status}; is SESSION_TOKEN a live session for this link?`);
  }

  const players = list.json('items').map((item) => item.id);
  if (players.length === 0) {
    fail('The link shows no published players.');
  }

  return { players };
}

/**
 * Picks the player for this iteration.
 * @param {{ players: string[] }} data the setup data.
 * @returns {string} a player id.
 */
function pick(data) {
  return data.players[exec.scenario.iterationInTest % data.players.length];
}

/** The gate card. */
export function gateCard() {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}`, { headers: PROXY, tags: { name: 'gate_card' } });
  check(response, { 'gate card 200': (r) => r.status === 200 });
}

/** A code that does not open. */
export function unknownLink() {
  const response = http.get(`${BASE_URL}/links/${UNKNOWN_CODE}`, { headers: PROXY, tags: { name: 'unknown_link' }, responseCallback: http.expectedStatuses(404) });
  check(response, { 'unknown link 404': (r) => r.status === 404 });
}

/** A wrong code for the recipient. */
export function wrongCode() {
  const response = http.post(`${BASE_URL}/links/${LINK_CODE}/verify`, JSON.stringify({ email: RECIPIENT, code: '000000' }), {
    headers: { ...PROXY, 'Content-Type': 'application/json' },
    tags: { name: 'wrong_code' },
    responseCallback: http.expectedStatuses(422),
  });
  check(response, { 'wrong code 422': (r) => r.status === 422 });
}

/** A device token the API never issued. */
export function staleDevice() {
  const response = http.post(`${BASE_URL}/links/${LINK_CODE}/session`, null, {
    headers: { ...PROXY, 'X-Viewer-Device': `k6-not-a-device-${exec.scenario.iterationInTest}-aaaaaaaaaaaaaaaaaaaa` },
    tags: { name: 'stale_device' },
    responseCallback: http.expectedStatuses(401),
  });
  check(response, { 'stale device 401': (r) => r.status === 401 });
}

/** A code request from a fresh forward address. */
export function emailCode() {
  const email = `k6-${Date.now()}-${exec.scenario.iterationInTest}@example.test`;
  const response = http.post(`${BASE_URL}/links/${LINK_CODE}/email-code`, JSON.stringify({ email }), {
    headers: { ...PROXY, 'Content-Type': 'application/json' },
    tags: { name: 'email_code' },
  });
  check(response, { 'email code 202': (r) => r.status === 202 });
}

/** Who the session belongs to. */
export function session() {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/session`, viewer('session'));
  check(response, { 'session 200': (r) => r.status === 200 });
}

/** The link overview. */
export function content() {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/content`, viewer('content'));
  check(response, { 'content 200': (r) => r.status === 200 && r.json('viewerEmail') !== '' });
}

/** The roster cards. */
export function players() {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/players?page=1&pageSize=20`, viewer('players'));
  check(response, { 'players 200': (r) => r.status === 200 });
}

/**
 * One player's header.
 * @param {{ players: string[] }} data the setup data.
 */
export function player(data) {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/players/${pick(data)}`, viewer('player'));
  check(response, { 'player 200': (r) => r.status === 200 });
}

/**
 * One player's videos.
 * @param {{ players: string[] }} data the setup data.
 */
export function videos(data) {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/players/${pick(data)}/videos?page=1&pageSize=100`, viewer('videos'));
  check(response, { 'videos 200': (r) => r.status === 200 });
}

/**
 * One player's documents.
 * @param {{ players: string[] }} data the setup data.
 */
export function documents(data) {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/players/${pick(data)}/documents?page=1&pageSize=100`, viewer('documents'));
  check(response, { 'documents 200': (r) => r.status === 200 });
}

/**
 * One player's photo; 404 is expected for a player without one.
 * @param {{ players: string[] }} data the setup data.
 */
export function photo(data) {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/players/${pick(data)}/photo`, viewer('photo', [200, 404]));
  check(response, { 'photo 200/404': (r) => r.status === 200 || r.status === 404 });
}

/** A content read without a session. */
export function noSession() {
  const response = http.get(`${BASE_URL}/links/${LINK_CODE}/content`, { headers: PROXY, tags: { name: 'no_session' }, responseCallback: http.expectedStatuses(401) });
  check(response, { 'no session 401': (r) => r.status === 401 });
}
