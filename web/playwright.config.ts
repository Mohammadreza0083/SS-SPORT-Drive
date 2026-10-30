import { defineConfig, devices } from "@playwright/test";

/** Port of the Next.js dev server the visual tests start (kept away from the usual 3000). */
const PORT = 3200;

/**
 * Playwright visual tests for the viewer pages (V-2 onwards). They run against the development-only
 * `/design/*` previews with motion reduced, so every screenshot is deterministic.
 *
 * Baselines are Linux screenshots: generate and check them in the Playwright Docker image (see README),
 * the same image CI uses, so fonts and anti-aliasing match.
 */
export default defineConfig({
  testDir: "./e2e",
  snapshotPathTemplate: "{testDir}/__screenshots__/{testFilePath}/{arg}{ext}",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.01, animations: "disabled", caret: "hide" },
  },
  use: {
    baseURL: `http://localhost:${PORT}`,
    reducedMotion: "reduce",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: `npm run dev -- --port ${PORT}`,
    url: `http://localhost:${PORT}`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
  },
});
