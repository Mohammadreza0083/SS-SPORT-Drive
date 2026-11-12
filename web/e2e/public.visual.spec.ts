import { expect, test } from "@playwright/test";

/** The canvas draws Home at 1440 x 1760 and Locked at 1440 x 2480; both refill at tablet and phone widths. */
const viewports = [
  { name: "desktop", width: 1440, height: 1180 },
  { name: "tablet", width: 834, height: 1112 },
  { name: "mobile", width: 390, height: 844 },
] as const;

for (const viewport of viewports) {
  test(`home matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto("/design/home");
    await page.evaluate(() => document.fonts.ready);
    await expect(page.getByRole("heading", { level: 1, name: "Find your next player." })).toBeVisible();

    await expect(page).toHaveScreenshot(`home-${viewport.name}.png`, { fullPage: true, mask: [page.locator("nextjs-portal")] });
  });

  test(`locked page matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto("/design/locked");
    await page.evaluate(() => document.fonts.ready);
    await expect(page.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeVisible();

    await expect(page).toHaveScreenshot(`locked-${viewport.name}.png`, { fullPage: true, mask: [page.locator("nextjs-portal")] });
  });

  test(`request-access dialog matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto("/design/locked");
    await page.evaluate(() => document.fonts.ready);
    await page.getByRole("button", { name: "Request access" }).first().click();
    await expect(page.getByRole("dialog", { name: "Request access" })).toBeVisible();

    await expect(page).toHaveScreenshot(`request-access-${viewport.name}.png`, { mask: [page.locator("nextjs-portal")] });
  });
}

test("the home search and chips filter the cards in place", async ({ page }) => {
  await page.goto("/design/home");
  const cards = page.getByRole("link", { name: /^View / });
  await expect(cards).toHaveCount(8);

  await page.getByRole("button", { name: "OH" }).click();
  await expect(cards).toHaveCount(3);

  await page.getByRole("searchbox", { name: "Search players by name" }).fill("kian");
  await expect(cards).toHaveCount(1);
});

test("the signed-in preview shows the viewer and Sign out (P-7)", async ({ page }) => {
  await page.goto("/design/home?signedIn=1");

  await expect(page.getByText("j.demir@aurora-volley.example")).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
});

test("the locked page loads no media at all (P-2)", async ({ page }) => {
  const media: string[] = [];
  page.on("request", (request) => {
    if (/\.(m3u8|ts|mp4|pdf|dvw)(\?|$)/i.test(request.url())) media.push(request.url());
  });

  await page.goto("/design/locked");
  await expect(page.getByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeVisible();

  expect(media).toEqual([]);
});

for (const path of ["/design/home", "/design/locked"]) {
  test(`${path} has no horizontal scroll on a phone`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBe(0);
  });
}
