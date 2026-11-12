import { expect, test } from "@playwright/test";

/** The canvas draws the Roster at 1440 px; the card grid refills at tablet and phone widths. */
const viewports = [
  { name: "desktop", width: 1440, height: 1180 },
  { name: "tablet", width: 834, height: 1112 },
  { name: "mobile", width: 390, height: 844 },
] as const;

for (const viewport of viewports) {
  test(`roster matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto("/design/roster");
    await page.evaluate(() => document.fonts.ready);
    await expect(page.getByRole("heading", { level: 1, name: "Roster" })).toBeVisible();

    await expect(page).toHaveScreenshot(`roster-${viewport.name}.png`, { fullPage: true, mask: [page.locator("nextjs-portal")] });
  });
}

test("the position chips filter the roster in place", async ({ page }) => {
  await page.goto("/design/roster");
  await expect(page.getByRole("link", { name: /View player/ })).toHaveCount(9);

  await page.getByRole("button", { name: "OH" }).click();

  await expect(page.getByRole("link", { name: /View player/ })).toHaveCount(3);
  await expect(page.getByRole("button", { name: "OH" })).toHaveAttribute("aria-pressed", "true");
});

test("roster has no horizontal scroll on a phone", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/design/roster");
  await expect(page.getByRole("heading", { level: 1, name: "Roster" })).toBeVisible();

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
});
