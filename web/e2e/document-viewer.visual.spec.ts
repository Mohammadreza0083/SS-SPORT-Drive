import { expect, test, type Page } from "@playwright/test";

/** The canvas's artboard sizes for the document viewer, plus tablet. */
const viewports = [
  { name: "desktop", width: 1440, height: 900 },
  { name: "tablet", width: 834, height: 1112 },
  { name: "mobile", width: 390, height: 844 },
] as const;

/**
 * Opens the player page preview and the "Season stats 2026" document.
 *
 * @param page - The browser page.
 * @returns The open dialog.
 */
async function openDocument(page: Page) {
  await page.route("**/*.m3u8", (route) => route.abort());
  await page.goto("/design/player-page");
  await page.evaluate(() => document.fonts.ready);
  // Put the card at the top first, so the page behind the viewer is always in the same place.
  const card = page.getByRole("button", { name: /Season stats 2026/ });
  await card.evaluate((element) => element.scrollIntoView({ block: "start" }));
  await card.click();
  const dialog = page.getByRole("dialog", { name: "Season stats 2026, view-only document" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("img", { name: "Page 1 of 2" })).toBeVisible();
  return dialog;
}

for (const viewport of viewports) {
  test(`document viewer matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await openDocument(page);

    await expect(page).toHaveScreenshot(`document-viewer-${viewport.name}.png`, {
      // The email overlay moves to a random spot; the dev indicator is not part of the page.
      mask: [page.locator(".ss-wm"), page.locator("nextjs-portal")],
    });
  });
}

test("phone viewer shows one page at a time and pages with Next", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const dialog = await openDocument(page);

  await expect(dialog.getByRole("img", { name: "Page 2 of 2" })).toBeHidden();
  await dialog.getByRole("button", { name: "Next page" }).click();
  await expect(dialog.getByRole("img", { name: "Page 2 of 2" })).toBeVisible();
  await expect(dialog.getByRole("img", { name: "Page 1 of 2" })).toBeHidden();
});

test("the viewer closes with Escape and prints nothing", async ({ page }) => {
  const dialog = await openDocument(page);

  await page.emulateMedia({ media: "print" });
  await expect(dialog).toBeHidden();
  await page.emulateMedia({ media: "screen" });

  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
