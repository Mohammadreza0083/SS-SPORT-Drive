import { expect, test, type Page } from "@playwright/test";

/** The canvas draws the gate at 390 px; desktop shows the same column centred. */
const viewports = [
  { name: "mobile", width: 390, height: 844 },
  { name: "desktop", width: 1440, height: 900 },
] as const;

/**
 * Opens the gate preview with the `/api/links/…` routes stubbed: no trusted device, codes sent, any code accepted.
 *
 * @param page - The browser page.
 */
async function openGate(page: Page) {
  await page.route("**/api/links/*/session", (route) => route.fulfill({ status: 401, json: { status: 401, code: "viewer.verification_required" } }));
  await page.route("**/api/links/*/email-code", (route) => route.fulfill({ status: 202, json: { email: "a***@aurora-volley.example", resendAfterSeconds: 42, expiresInSeconds: 600 } }));
  await page.route("**/api/links/*/verify", (route) => route.fulfill({ json: { ok: true } }));
  await page.goto("/design/gate");
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByRole("heading", { name: "Confirm it's you" })).toBeVisible();
}

for (const viewport of viewports) {
  test(`gate email step matches the canvas on ${viewport.name} (${viewport.width}px)`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await openGate(page);
    await page.getByLabel("Email").fill("a.yilmaz@aurora-volley.example");

    await expect(page).toHaveScreenshot(`gate-email-${viewport.name}.png`, { mask: [page.locator("nextjs-portal")] });
  });
}

test("gate code step with a short code matches the canvas (390px)", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openGate(page);
  await page.getByLabel("Email").fill("a.yilmaz@aurora-volley.example");
  await page.getByRole("button", { name: "Send code" }).click();
  await expect(page.getByRole("heading", { name: "Check your inbox" })).toBeVisible();
  await page.getByLabel("6-digit code").fill("042");
  await page.getByRole("button", { name: "Watch" }).click();
  await expect(page.getByText("Enter all 6 digits.")).toBeVisible();

  // The resend countdown ticks with real time.
  await expect(page).toHaveScreenshot("gate-code-mobile.png", { mask: [page.locator(".gate-row > span"), page.locator("nextjs-portal")] });
});

test("gate You're in step matches the canvas (390px)", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openGate(page);
  await page.getByLabel("Email").fill("a.yilmaz@aurora-volley.example");
  await page.getByRole("button", { name: "Send code" }).click();
  await page.getByLabel("6-digit code").fill("042917");
  await page.getByRole("button", { name: "Watch" }).click();
  await expect(page.getByRole("heading", { name: "You're in" })).toBeVisible();

  await expect(page).toHaveScreenshot("gate-done-mobile.png", { mask: [page.locator("nextjs-portal")] });
});

test("link not available page (390px)", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/design/gate-unavailable");
  await page.evaluate(() => document.fonts.ready);

  await expect(page).toHaveScreenshot("gate-unavailable-mobile.png", { mask: [page.locator("nextjs-portal")] });
});
