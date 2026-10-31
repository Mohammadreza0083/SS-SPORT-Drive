import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import MiniAppLayout from "./layout";
import { MiniAppHome } from "./mini-app-home";
import { MyPlayersList } from "./players/my-players-list";
import { NewPlayerForm } from "./new/new-player-form";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

/**
 * Answers the Mini App's API calls with the given JSON, by path.
 *
 * @param routes - Response bodies keyed by the start of the path.
 */
function api(routes: Record<string, unknown>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      const key = Object.keys(routes).find((route) => url.startsWith(route));
      return key ? Response.json(routes[key]) : new Response(null, { status: 404 });
    }),
  );
}

describe("Mini App UI (T-9)", () => {
  it("wraps every Mini App page in the themed root", () => {
    const { container } = render(
      <MiniAppLayout params={Promise.resolve({})}>
        <p>page</p>
      </MiniAppLayout>,
    );

    expect(container.querySelector(".ma")?.textContent).toBe("page");
  });

  it("shows the home as a greeting card and two action tiles, New player in red", async () => {
    vi.stubGlobal("Telegram", { WebApp: { initData: "user=1&hash=ab", ready: vi.fn() } });
    api({ "/api/miniapp/me": { id: "1", telegramId: 1, displayName: "Reza Ahmadi", username: "reza", role: "Analyst" } });

    render(<MiniAppHome telegramReady />);

    const newPlayer = await screen.findByRole("link", { name: /New player/ });
    expect(newPlayer.className).toContain("ma-tile");
    expect(newPlayer.className).toContain("ma-tile-accent");
    expect(screen.getByRole("link", { name: /My players/ }).className).toContain("ma-tile");
    expect(screen.getByText("Analyst").className).toContain("ma-role");
  });

  it("marks each player's status with its colour and shows initials", async () => {
    api({
      "/api/miniapp/players": {
        items: [
          { id: "a", name: "Arash Tehrani", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["OPP"], hasPhoto: true, status: "Published", isComplete: true, fileCount: 6, processingCount: 0, failedCount: 0, hasLostFolder: false, needsAttention: false, createdAt: "2026-10-01" },
          { id: "b", name: "Kian Farhadi", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["OH"], hasPhoto: false, status: "Waiting", isComplete: false, fileCount: 0, processingCount: 0, failedCount: 0, hasLostFolder: false, needsAttention: false, createdAt: "2026-10-02" },
          { id: "c", name: "Milad Sadeghi", category: "Men", nationalityCode: "IR", nationalityName: "Iran", positions: ["MB"], hasPhoto: true, status: "Published", isComplete: true, fileCount: 3, processingCount: 0, failedCount: 0, hasLostFolder: true, needsAttention: true, createdAt: "2026-10-03" },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 3,
        totalPages: 1,
      },
    });

    render(<MyPlayersList initData="user=1&hash=ab" />);

    expect((await screen.findByText("Live", { selector: "span" })).className).toContain("ma-pill-live");
    expect(screen.getByText("Waiting for an admin").className).toContain("ma-pill-waiting");
    expect(screen.getByText("Needs attention", { selector: "span" }).className).toContain("ma-pill-attention");
    expect(screen.getByText("AT").className).toContain("ma-avatar");
  });

  it("makes Create player the screen's main action", () => {
    api({ "/api/miniapp/countries": [] });

    render(<NewPlayerForm initData="user=1&hash=ab" />);

    expect(screen.getByRole("button", { name: "Create player" }).className).toContain("ma-primary");
    expect(screen.getByRole("group", { name: "Category" }).className).toContain("ma-segment");
    expect(screen.getByRole("group", { name: "Positions" }).className).toContain("ma-positions");
  });
});
