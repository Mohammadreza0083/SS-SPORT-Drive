/**
 * k6 benchmark for POST /telegram/webhook (backlog T-1).
 *
 * Three traffic shapes run side by side, as Telegram and the internet produce them:
 *  - new_updates:   fresh updates (messages and button presses), each with a new update_id → 200, one row each.
 *  - redeliveries:  updates Telegram sends again (same update_id) → 200, skipped by the de-duplication.
 *  - wrong_secret:  calls without the right secret token (scanners, mistakes) → 401, no database work.
 *
 * Usage (see perf/k6/README.md):
 *   k6 run -e BASE_URL=http://localhost:5080 -e WEBHOOK_SECRET=... -e PROFILE=smoke perf/k6/telegram-webhook.js
 *
 * Environment:
 *   BASE_URL        API origin (default http://localhost:5080)
 *   WEBHOOK_SECRET  must equal the API's Telegram__WebhookSecret (required)
 *   PROFILE         smoke (default, ~30 s, light) or load (~4 min, ramps to the peak rate)
 *   PEAK_RATE       updates per second at the peak of the load profile (default 50)
 */
import http from 'k6/http';
import exec from 'k6/execution';
import { check, fail } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
const SECRET = __ENV.WEBHOOK_SECRET || '';
const PROFILE = (__ENV.PROFILE || 'smoke').toLowerCase();
const PEAK_RATE = Number(__ENV.PEAK_RATE || 50);
const WEBHOOK_URL = `${BASE_URL}/telegram/webhook`;
const SECRET_HEADER = 'X-Telegram-Bot-Api-Secret-Token';

/** How many already-delivered updates the redelivery scenario replays. */
const REDELIVERY_POOL = 25;

/**
 * Builds the scenario set for a profile.
 * @param {string} profile smoke or load.
 * @returns {object} k6 scenarios.
 */
function scenariosFor(profile) {
  if (profile === 'load') {
    return {
      new_updates: {
        executor: 'ramping-arrival-rate',
        exec: 'newUpdate',
        startRate: 1,
        timeUnit: '1s',
        preAllocatedVUs: 20,
        maxVUs: 200,
        stages: [
          { target: PEAK_RATE, duration: '1m' },
          { target: PEAK_RATE, duration: '2m' },
          { target: 0, duration: '30s' },
        ],
      },
      redeliveries: {
        executor: 'constant-arrival-rate',
        exec: 'redelivery',
        rate: Math.max(1, Math.round(PEAK_RATE / 5)),
        timeUnit: '1s',
        duration: '3m30s',
        preAllocatedVUs: 5,
        maxVUs: 50,
      },
      wrong_secret: {
        executor: 'constant-arrival-rate',
        exec: 'wrongSecret',
        rate: Math.max(1, Math.round(PEAK_RATE / 10)),
        timeUnit: '1s',
        duration: '3m30s',
        preAllocatedVUs: 2,
        maxVUs: 20,
      },
    };
  }

  return {
    new_updates: { executor: 'constant-arrival-rate', exec: 'newUpdate', rate: 5, timeUnit: '1s', duration: '30s', preAllocatedVUs: 5, maxVUs: 20 },
    redeliveries: { executor: 'constant-arrival-rate', exec: 'redelivery', rate: 2, timeUnit: '1s', duration: '30s', preAllocatedVUs: 2, maxVUs: 10 },
    wrong_secret: { executor: 'constant-arrival-rate', exec: 'wrongSecret', rate: 1, timeUnit: '1s', duration: '30s', preAllocatedVUs: 1, maxVUs: 5 },
  };
}

export const options = {
  scenarios: scenariosFor(PROFILE),
  thresholds: {
    // Every response has the status its scenario expects (200, or 401 for a wrong secret).
    checks: ['rate>0.99'],
    // 401 is expected in wrong_secret and marked as such, so this counts real failures only.
    http_req_failed: ['rate<0.01'],
    // A new update records its id in PostgreSQL; a repeat or a wrong secret does less work.
    'http_req_duration{scenario:new_updates}': ['p(95)<250', 'p(99)<500'],
    'http_req_duration{scenario:redeliveries}': ['p(95)<200'],
    'http_req_duration{scenario:wrong_secret}': ['p(95)<100'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

/**
 * Builds a Telegram update. Even ids are text messages, odd ids are button presses.
 * @param {number} updateId the update_id.
 * @returns {string} the JSON body.
 */
function updateBody(updateId) {
  const user = { id: 100000000 + (updateId % 5000), is_bot: false, first_name: 'Analyst', username: `analyst_${updateId % 5000}` };
  const chat = { id: user.id, type: 'private' };
  const date = Math.floor(Date.now() / 1000);
  const update = updateId % 2 === 0
    ? { update_id: updateId, message: { message_id: updateId % 100000, date, chat, from: user, text: '/start' } }
    : {
      update_id: updateId,
      callback_query: {
        id: `${updateId}`,
        from: user,
        chat_instance: `${user.id}`,
        data: 'access:request',
        message: { message_id: updateId % 100000, date, chat, text: 'Request access?' },
      },
    };
  return JSON.stringify(update);
}

/**
 * Posts one update.
 * @param {number} updateId the update_id.
 * @param {string} secret the secret header value.
 * @param {object} [extra] extra request params.
 * @returns {object} the k6 response.
 */
function post(updateId, secret, extra = {}) {
  return http.post(WEBHOOK_URL, updateBody(updateId), {
    headers: { 'Content-Type': 'application/json', [SECRET_HEADER]: secret },
    ...extra,
  });
}

/**
 * Checks the secret, picks an id range unique to this run, and delivers the pool the redelivery scenario replays.
 * @returns {{ base: number, pool: number[] }} shared test data.
 */
export function setup() {
  if (!SECRET) {
    fail('Set WEBHOOK_SECRET to the API\'s Telegram__WebhookSecret.');
  }

  // Real update_ids are 32-bit, so stay inside that range: each minute of the clock gets its own window of
  // 60 000 ids (enough for a full load run), cycling every 12.5 days. Runs started in the same minute share a window.
  const base = 1000000000 + (Math.floor(Date.now() / 60000) % 18000) * 60000 + REDELIVERY_POOL;
  const pool = [];
  for (let i = 0; i < REDELIVERY_POOL; i += 1) {
    const id = base - 1 - i;
    const response = post(id, SECRET);
    if (response.status !== 200) {
      fail(`Setup delivery failed with ${response.status}: is the API up and the secret right? ${response.body}`);
    }
    pool.push(id);
  }

  return { base, pool };
}

/**
 * new_updates: a fresh update, answered 200.
 * @param {{ base: number }} data shared test data.
 */
export function newUpdate(data) {
  const response = post(data.base + exec.scenario.iterationInTest, SECRET);
  check(response, { 'new update: 200': (r) => r.status === 200 });
}

/**
 * redeliveries: an update that was already delivered, answered 200 and skipped.
 * @param {{ pool: number[] }} data shared test data.
 */
export function redelivery(data) {
  const id = data.pool[exec.scenario.iterationInTest % data.pool.length];
  const response = post(id, SECRET);
  check(response, { 'redelivery: 200': (r) => r.status === 200 });
}

/**
 * wrong_secret: a call with a wrong secret, answered 401 (expected, so not a failed request).
 * @param {{ base: number }} data shared test data.
 */
export function wrongSecret(data) {
  const response = post(data.base - REDELIVERY_POOL - 1 - exec.scenario.iterationInTest, 'not-the-secret', {
    responseCallback: http.expectedStatuses(401),
  });
  check(response, { 'wrong secret: 401': (r) => r.status === 401 });
}
