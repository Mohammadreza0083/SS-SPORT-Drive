/** One `headers()` rule of the Next.js config. */
export type HeaderRule = { source: string; headers: { key: string; value: string }[] };

/** Our own subdomains (media, HLS and image delivery through Cloudflare). */
const OWN_SUBDOMAINS = "https://*.ss-sport.co";

/** Telegram Web, the only page allowed to frame the Mini App. */
const TELEGRAM_WEB = "https://web.telegram.org";

/** Where the Mini App loads `telegram-web-app.js` from. */
const TELEGRAM_SCRIPTS = "https://telegram.org";

/** Every page except the Mini App (path-to-regexp negative look-ahead). */
const SITE_SOURCE = "/:path((?!miniapp(?:/|$)).*)";

/**
 * Builds a Content-Security-Policy. Scripts may run inline (owner decision, 2026-11-01: a static CSP keeps cached
 * and prerendered pages working, so no per-request nonce); everything else is limited to our own origins.
 *
 * @param options - `isDev` adds eval and hot-reload sockets; `scripts` and `frameAncestors` vary per area.
 * @returns The policy as one header value.
 */
function contentSecurityPolicy(options: { isDev: boolean; scripts: string[]; frameAncestors: string }): string {
  const { isDev, scripts, frameAncestors } = options;
  const directives = [
    "default-src 'self'",
    `script-src ${["'self'", "'unsafe-inline'", ...(isDev ? ["'unsafe-eval'"] : []), ...scripts].join(" ")}`,
    "style-src 'self' 'unsafe-inline'",
    `img-src 'self' data: blob: ${OWN_SUBDOMAINS}`,
    `media-src 'self' blob: ${OWN_SUBDOMAINS}`,
    `connect-src ${["'self'", OWN_SUBDOMAINS, ...(isDev ? ["ws:"] : [])].join(" ")}`,
    "font-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    `frame-ancestors ${frameAncestors}`,
    ...(isDev ? [] : ["upgrade-insecure-requests"]),
  ];
  return directives.join("; ");
}

/**
 * The security headers of the web app (decisions §1b), for `headers()` in `next.config.ts`.
 *
 * - Every page: no MIME sniffing, a cross-origin-safe referrer, no powerful browser features, an isolated window,
 *   and HSTS outside development.
 * - The site: may not be framed at all.
 * - The Mini App (`/miniapp`): may be framed by Telegram Web only, and may load Telegram's script.
 *
 * @param options - `isDev` is true under `next dev`.
 * @returns The header rules, in the order Next.js should apply them.
 */
export function securityHeaders(options: { isDev: boolean }): HeaderRule[] {
  const { isDev } = options;
  const base = [
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()" },
    { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
    ...(isDev ? [] : [{ key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" }]),
  ];
  const miniApp = [
    {
      key: "Content-Security-Policy",
      value: contentSecurityPolicy({ isDev, scripts: [TELEGRAM_SCRIPTS], frameAncestors: TELEGRAM_WEB }),
    },
  ];

  return [
    { source: "/:path*", headers: base },
    {
      source: SITE_SOURCE,
      headers: [
        { key: "Content-Security-Policy", value: contentSecurityPolicy({ isDev, scripts: [], frameAncestors: "'none'" }) },
        { key: "X-Frame-Options", value: "DENY" },
      ],
    },
    { source: "/miniapp", headers: miniApp },
    { source: "/miniapp/:path*", headers: miniApp },
  ];
}
