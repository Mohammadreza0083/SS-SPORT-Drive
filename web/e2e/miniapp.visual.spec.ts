import { expect, test, type Page } from "@playwright/test";

/** The canvas's Mini App artboard size (a phone inside Telegram). */
const PHONE = { width: 390, height: 844 };

/** Stands in for Telegram's script: signed initData plus the dark theme Telegram would set. */
const TELEGRAM_STUB = `
  window.Telegram = { WebApp: { initData: "user=1&hash=ab", ready() {}, expand() {}, themeParams: {} } };
  const theme = { "bg-color": "#17212b", "text-color": "#f5f5f5", "hint-color": "#708499",
    "secondary-bg-color": "#232e3c", "link-color": "#6ab3f3" };
  for (const [name, value] of Object.entries(theme)) {
    document.documentElement.style.setProperty("--tg-theme-" + name, value);
  }
`;

/** Rows of My players: one per status colour. */
const players = [
  { id: "p1", name: "Arash Tehrani", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["OPP"], hasPhoto: true, status: "Published", isComplete: true, fileCount: 6, processingCount: 0, failedCount: 0, hasLostFolder: false, needsAttention: false, createdAt: "2026-10-01" },
  { id: "p2", name: "Kian Farhadi", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["OH", "L"], hasPhoto: false, status: "Waiting", isComplete: false, fileCount: 2, processingCount: 1, failedCount: 0, hasLostFolder: false, needsAttention: false, createdAt: "2026-10-02" },
  { id: "p3", name: "Milad Sadeghi", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["MB"], hasPhoto: true, status: "Published", isComplete: true, fileCount: 3, processingCount: 0, failedCount: 1, hasLostFolder: true, needsAttention: true, createdAt: "2026-10-03" },
];

/** The player card the detail and edit sheet show. */
const player = {
  id: "p1", name: "Arash Tehrani", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["OPP"],
  status: "Published", analystName: "Reza Ahmadi", hasPhoto: false, folderCount: 2,
  folders: [{ id: "f1", name: "Arash Tehrani 2026", access: "Ok" }, { id: "f2", name: "Highlights", access: "Ok" }],
  canEditDetails: true, canChangePhoto: true, canEditFiles: true, canHideFiles: true,
};

/** The player's files. */
const files = [
  { id: "v1", kind: "Video", title: "Highlights 2026", tags: ["Attack", "Serve"], sortOrder: 0, visibility: "Visible", state: "Ready", durationSeconds: 192, isHero: true },
  { id: "v2", kind: "Video", title: "Full match vs Sada", tags: [], sortOrder: 1, visibility: "Visible", state: "Ready", durationSeconds: 5400, isHero: false },
  { id: "d1", kind: "Document", title: "Season stats 2026", tags: [], sortOrder: 2, visibility: "Visible", state: "Ready", pageCount: 2, isHero: false },
];

/** A page of a paged list. */
const pageOf = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });

/**
 * Opens a Mini App screen as Telegram would, with the API answered from the fixtures above.
 *
 * @param page - The browser page.
 * @param path - The Mini App path.
 */
async function openMiniApp(page: Page, path: string) {
  await page.setViewportSize(PHONE);
  await page.route("https://telegram.org/js/telegram-web-app.js", (route) =>
    route.fulfill({ contentType: "text/javascript", body: TELEGRAM_STUB }),
  );
  await page.route("**/api/miniapp/**", (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname.replace("/api/miniapp", "");
    const json = (body: unknown) => route.fulfill({ json: body });
    if (path === "/me") return json({ id: "u1", telegramId: 1, displayName: "Reza Ahmadi", username: "reza", role: "Analyst" });
    if (path === "/countries") return json([{ code: "IR", name: "Iran" }, { code: "IT", name: "Italy" }]);
    if (path === "/volleybox") return json({ volleyboxId: 104233, existingPlayerId: null, existingPlayerName: null });
    if (path === "/players") return json(pageOf(players));
    if (path === "/players/p1") return json(player);
    if (path === "/players/p1/files") return json(pageOf(files));
    return route.fulfill({ status: 404 });
  });
  await page.goto(path);
  await page.evaluate(() => document.fonts.ready);
}

test("Mini App home matches the MiniHome artboard", async ({ page }) => {
  await openMiniApp(page, "/miniapp");
  await expect(page.getByRole("link", { name: /New player/ })).toBeVisible();

  await expect(page).toHaveScreenshot("miniapp-home.png", { mask: [page.locator("nextjs-portal")] });
});

test("New player matches the MiniNewPlayer artboard", async ({ page }) => {
  await page.addInitScript(() =>
    window.localStorage.setItem(
      "ssd.miniapp.newPlayerDraft",
      JSON.stringify({ category: "Men", name: "Arash Tehrani", nationalityCode: "IR", positions: ["OPP", "OH"], volleyboxUrl: "", driveFolderUrl: "" }),
    ),
  );
  await openMiniApp(page, "/miniapp/new");
  await expect(page.getByRole("button", { name: "Create player" })).toBeVisible();
  await expect(page.getByRole("button", { name: "OPP", exact: true })).toBeVisible();

  await expect(page).toHaveScreenshot("miniapp-new-player.png", { fullPage: true, mask: [page.locator("nextjs-portal")] });
});

test("My players matches the MiniPlayers artboard", async ({ page }) => {
  await openMiniApp(page, "/miniapp/players");
  await expect(page.getByText("Arash Tehrani")).toBeVisible();

  await expect(page).toHaveScreenshot("miniapp-players.png", { mask: [page.locator("nextjs-portal")] });
});

test("Player detail matches the MiniPlayer artboard", async ({ page }) => {
  await openMiniApp(page, "/miniapp/players/p1");
  await expect(page.getByRole("listitem", { name: "file Highlights 2026" })).toBeVisible();
  // The badges and icons are drawn by CSS; the names screen readers hear must stay exactly the same.
  await expect(page.getByRole("button", { name: "Edit Highlights 2026", exact: true })).toBeVisible();

  await expect(page).toHaveScreenshot("miniapp-player.png", { fullPage: true, mask: [page.locator("nextjs-portal")] });
});

test("The edit-video sheet matches the MiniPlayer artboard's sheet", async ({ page }) => {
  await openMiniApp(page, "/miniapp/players/p1");
  await page.getByRole("listitem", { name: "file Highlights 2026" }).getByRole("button").click();
  await expect(page.getByRole("dialog")).toBeVisible();

  await expect(page).toHaveScreenshot("miniapp-edit-video.png", { mask: [page.locator("nextjs-portal")] });
});
