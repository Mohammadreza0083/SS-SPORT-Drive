import { expect, test } from "@playwright/test";

/** The design canvas's three artboard widths for the player page. */
const viewports = [
  { name: "desktop", width: 1440, height: 900 },
  { name: "tablet", width: 834, height: 1112 },
  { name: "mobile", width: 390, height: 844 },
] as const;

for (const viewport of viewports) {
  test(`player page matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    // No network video in screenshots: the player shows its pre-play state.
    await page.route("**/*.m3u8", (route) => route.abort());
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto("/design/player-page");
    await page.evaluate(() => document.fonts.ready);
    await expect(page.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeVisible();
    await expect(page.locator(".ss-player-big")).toBeVisible();

    await expect(page).toHaveScreenshot(`player-page-${viewport.name}.png`, {
      fullPage: true,
      // The Next.js dev indicator is not part of the page; the email overlay moves to a random spot.
      mask: [page.locator("nextjs-portal"), page.locator(".ss-wm")],
    });
  });
}

test("the viewer's email lies over the video, faint and unclickable", async ({ page }) => {
  await page.route("**/*.m3u8", (route) => route.abort());
  await page.goto("/design/player-page");

  const overlay = page.locator(".ss-player-frame .ss-wm");
  await expect(overlay).toHaveText("j.demir@aurora-volley.example");
  await expect(overlay).toHaveCSS("opacity", "0.2");
  await expect(overlay).toHaveCSS("pointer-events", "none");
});

test("player page has no horizontal scroll on a phone", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/design/player-page");

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
});
