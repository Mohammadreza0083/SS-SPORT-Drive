import { existsSync } from "node:fs";
import path from "node:path";
import type { NextConfig } from "next";
import { securityHeaders } from "./src/lib/security-headers";

/**
 * Local runs (`npm run dev` inside web/) share the repository's root .env with the API.
 * Values already in the environment win, and Docker builds have no root .env, so this is skipped there.
 */
const rootEnvFile = path.resolve(process.cwd(), "..", ".env");
if (existsSync(rootEnvFile)) {
  process.loadEnvFile(rootEnvFile);
}

/**
 * Next.js configuration. `standalone` output produces a minimal server bundle for the Docker image.
 * The Playwright visual tests set `VISUAL_TESTS=1`: the dev indicator and its toasts come and go with timing,
 * so they are switched off there to keep screenshots stable.
 * Every response gets the security headers (decisions §1b); `X-Powered-By` is not sent.
 */
const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  poweredByHeader: false,
  headers: async () => securityHeaders({ isDev: process.env.NODE_ENV === "development" }),
  ...(process.env.VISUAL_TESTS === "1" ? { devIndicators: false as const } : {}),
};

export default nextConfig;
