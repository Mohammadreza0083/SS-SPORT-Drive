import { existsSync } from "node:fs";
import path from "node:path";
import type { NextConfig } from "next";

/**
 * Local runs (`npm run dev` inside web/) share the repository's root .env with the API.
 * Values already in the environment win, and Docker builds have no root .env, so this is skipped there.
 */
const rootEnvFile = path.resolve(process.cwd(), "..", ".env");
if (existsSync(rootEnvFile)) {
  process.loadEnvFile(rootEnvFile);
}

/** Next.js configuration. `standalone` output produces a minimal server bundle for the Docker image. */
const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
};

export default nextConfig;
