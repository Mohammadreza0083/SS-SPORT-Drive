import { describe, expect, it } from "vitest";
import { securityHeaders } from "./security-headers";

/** The headers a rule sets, as a name → value map. */
function headersOf(rules: ReturnType<typeof securityHeaders>, source: string): Record<string, string> {
  const rule = rules.find((candidate) => candidate.source === source);
  if (!rule) {
    throw new Error(`No header rule for ${source}`);
  }
  return Object.fromEntries(rule.headers.map(({ key, value }) => [key, value]));
}

/** Splits a CSP into directive → value. */
function directives(csp: string): Record<string, string> {
  return Object.fromEntries(
    csp
      .split(";")
      .map((part) => part.trim())
      .filter(Boolean)
      .map((part) => {
        const [name, ...values] = part.split(/\s+/);
        return [name, values.join(" ")];
      }),
  );
}

const ALL = "/:path*";
const SITE = "/:path((?!miniapp(?:/|$)).*)";
const MINIAPP = "/miniapp/:path*";

describe("securityHeaders (decisions §1b)", () => {
  const production = securityHeaders({ isDev: false });
  const development = securityHeaders({ isDev: true });

  it("sends the base headers on every page", () => {
    const base = headersOf(production, ALL);
    expect(base["X-Content-Type-Options"]).toBe("nosniff");
    expect(base["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(base["Permissions-Policy"]).toContain("camera=()");
    expect(base["Permissions-Policy"]).toContain("geolocation=()");
    expect(base["Cross-Origin-Opener-Policy"]).toBe("same-origin");
  });

  it("sends HSTS in production only", () => {
    expect(headersOf(production, ALL)["Strict-Transport-Security"]).toBe("max-age=31536000; includeSubDomains");
    expect(headersOf(development, ALL)["Strict-Transport-Security"]).toBeUndefined();
  });

  it("forbids framing the site and locks its CSP to our own origins", () => {
    const site = headersOf(production, SITE);
    expect(site["X-Frame-Options"]).toBe("DENY");
    const csp = directives(site["Content-Security-Policy"]);
    expect(csp["default-src"]).toBe("'self'");
    expect(csp["frame-ancestors"]).toBe("'none'");
    expect(csp["object-src"]).toBe("'none'");
    expect(csp["base-uri"]).toBe("'self'");
    expect(csp["form-action"]).toBe("'self'");
    // Owner, 2026-11-01: static CSP with 'unsafe-inline' scripts, so cached and prerendered pages keep working.
    // P-8: the request-access dialog loads the Cloudflare Turnstile widget (a script and an iframe), and nothing else.
    expect(csp["script-src"]).toBe("'self' 'unsafe-inline' https://challenges.cloudflare.com");
    expect(csp["frame-src"]).toBe("'self' https://challenges.cloudflare.com");
    expect(csp["connect-src"]).toBe("'self' https://*.ss-sport.co");
    expect(csp["media-src"]).toBe("'self' blob: https://*.ss-sport.co");
    expect(csp).toHaveProperty("upgrade-insecure-requests");
  });

  it("lets Telegram Web frame the Mini App and load its script, and nothing else", () => {
    const miniapp = headersOf(production, MINIAPP);
    expect(miniapp["X-Frame-Options"]).toBeUndefined();
    const csp = directives(miniapp["Content-Security-Policy"]);
    expect(csp["frame-ancestors"]).toBe("https://web.telegram.org");
    expect(csp["script-src"]).toBe("'self' 'unsafe-inline' https://telegram.org");
    expect(csp["frame-src"]).toBeUndefined();
    expect(csp["default-src"]).toBe("'self'");
  });

  it("also covers the Mini App's own root path", () => {
    expect(production.some((rule) => rule.source === "/miniapp")).toBe(true);
    expect(headersOf(production, "/miniapp")).toEqual(headersOf(production, MINIAPP));
  });

  it("allows eval and hot reload in development only", () => {
    const dev = directives(headersOf(development, SITE)["Content-Security-Policy"]);
    expect(dev["script-src"]).toContain("'unsafe-eval'");
    expect(dev["connect-src"]).toContain("ws:");
    expect(dev).not.toHaveProperty("upgrade-insecure-requests");

    const prod = directives(headersOf(production, SITE)["Content-Security-Policy"]);
    expect(prod["script-src"]).not.toContain("'unsafe-eval'");
    expect(prod["connect-src"]).not.toContain("ws:");
  });
});
