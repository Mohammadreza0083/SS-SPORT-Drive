import type { NextConfig } from "next";

/** Next.js configuration. `standalone` output produces a minimal server bundle for the Docker image. */
const nextConfig: NextConfig = {
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
};

export default nextConfig;
