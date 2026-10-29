/**
 * k6 benchmark for the Telegram Mini App API (backlog T-3, T-4a).
 *
 * Every request signs in with Telegram-signed initData (`Authorization: tma <initData>`), built here exactly as
 * Telegram builds it, so the API does its full HMAC check and user lookup on each call. Six traffic shapes run side by side:
 *  - profile:        GET  /miniapp/me                      → 200 (signature + user lookup + profile read)
 *  - countries:      GET  /miniapp/countries               → 200 (245 countries from memory)
 *  - volleybox:      GET  /miniapp/volleybox?url=…         → 200 (parse + one indexed lookup)
 *  - create_player:  POST /miniapp/players                 → 201 (3 duplicate checks + 2 inserts in one transaction)
 *  - forged:         GET  /miniapp/me with a bad signature → 401 (no database work)
 *  - not_member:     GET  /miniapp/me as a stranger        → 403 (signature + one lookup)
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
 */
import http from 'k6/http';
import crypto from 'k6/crypto';
import exec from 'k6/execution';
import { check, fail } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const BOT_TOKEN = __ENV.BOT_TOKEN || '';
const ANALYST_ID = Number(__ENV.ANALYST_TELEGRAM_ID || 0);
const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const PEAK_RATE = Number(__ENV.PEAK_RATE || 50);

/** A Telegram id nobody in the team has (the 403 scenario). */
const STRANGER_ID = 999000001;

/** Letters for the names of created players; names allow Latin letters only. */
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
    // Opening the Mini App and moving around it reads the profile most; the form calls are rarer.
    profile: scenario('profile', 10, 1),
    countries: scenario('countries', 3, 0.2),
    volleybox: scenario('volleybox', 5, 0.4),
    create_player: scenario('createPlayer', 2, 0.1),
    forged: scenario('forged', 2, 0.2),
    not_member: scenario('notMember', 1, 0.1),
  },
  thresholds: {
    // Every response has the status its scenario expects (200/201, or 401/403 where that is the point).
    checks: ['rate>0.99'],
    // 401 and 403 are expected in their scenarios and marked as such, so this counts real failures only.
    http_req_failed: ['rate<0.01'],
    'http_req_duration{scenario:profile}': ['p(95)<150', 'p(99)<300'],
    'http_req_duration{scenario:countries}': ['p(95)<150'],
    'http_req_duration{scenario:volleybox}': ['p(95)<150'],
    'http_req_duration{scenario:create_player}': ['p(95)<400', 'p(99)<800'],
    'http_req_duration{scenario:forged}': ['p(95)<100'],
    'http_req_duration{scenario:not_member}': ['p(95)<150'],
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
  return { headers: { Authorization: `tma ${data}`, 'Content-Type': 'application/json' }, ...extra };
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

  // Volleybox ids are 32-bit: each minute of the clock gets its own window of 100 000 ids. Runs started in the same
  // minute share a window, so wait a minute between runs.
  return { base: 100000000 + (Math.floor(Date.now() / 60000) % 18000) * 100000 };
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
