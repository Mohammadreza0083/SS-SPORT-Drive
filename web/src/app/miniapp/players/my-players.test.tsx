import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MyPlayersList } from "./my-players-list";
import { PlayerDetail } from "./player-detail";

const PLAYER_ID = "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e";

/**
 * Builds a list row as the API returns it.
 *
 * @param changes - Fields to change.
 * @returns The row.
 */
function row(changes: Record<string, unknown> = {}) {
  return {
    id: PLAYER_ID,
    name: "Arash Tehrani",
    category: "Men",
    nationalityCode: "IR",
    nationalityName: "Iran",
    positions: ["OH", "OPP"],
    hasPhoto: false,
    status: "Published",
    isComplete: true,
    fileCount: 14,
    processingCount: 2,
    failedCount: 0,
    hasLostFolder: false,
    needsAttention: false,
    createdAt: "2026-10-29T13:30:00+00:00",
    ...changes,
  };
}

/**
 * Wraps rows in the API's page shape.
 *
 * @param items - The rows.
 * @param page - The page number.
 * @param totalPages - How many pages there are.
 * @returns The page.
 */
function paged(items: unknown[], page = 1, totalPages = 1) {
  return { items, page, pageSize: 20, totalCount: items.length, totalPages };
}

/**
 * Stubs fetch with a function of the requested URL.
 *
 * @param answer - Builds the response body for a URL.
 * @returns The fetch stub.
 */
function stubFetch(answer: (url: string) => unknown) {
  const fetchMock = vi.fn((url: string) => Promise.resolve(Response.json(answer(url))));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("MyPlayersList", () => {
  it("lists the players with their status and file counts, linking to each player", async () => {
    stubFetch(() => paged([row(), row({ id: "p2", name: "Kian Moradi", status: "Waiting", needsAttention: true, failedCount: 1, fileCount: 3, processingCount: 0 })]));

    render(<MyPlayersList initData="x" />);

    const link = await screen.findByRole("link", { name: /Arash Tehrani/ });
    expect(link.getAttribute("href")).toBe(`/miniapp/players/${PLAYER_ID}`);
    expect(link.textContent).toMatch(/Live/);
    expect(link.textContent).toMatch(/14 files · 2 processing/);
    expect(screen.getByRole("link", { name: /Kian Moradi/ }).textContent).toMatch(/Needs attention/);
  });

  it("sends the search and the chosen chip, starting again from page 1", async () => {
    const fetchMock = stubFetch(() => paged([row()]));

    render(<MyPlayersList initData="x" />);
    await screen.findByRole("link", { name: /Arash Tehrani/ });

    fireEvent.click(screen.getByRole("button", { name: "Needs attention" }));
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "ara" } });

    await waitFor(() => expect(fetchMock).toHaveBeenLastCalledWith("/api/miniapp/players?page=1&filter=needsAttention&search=ara", expect.anything()));
    expect(screen.getByRole("button", { name: "Needs attention" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("pages with Next and Previous", async () => {
    const fetchMock = stubFetch((url) => (url.includes("page=2") ? paged([row({ id: "p3", name: "Second Page" })], 2, 2) : paged([row()], 1, 2)));

    render(<MyPlayersList initData="x" />);
    await screen.findByRole("link", { name: /Arash Tehrani/ });
    expect((screen.getByRole("button", { name: "Previous" }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(await screen.findByRole("link", { name: /Second Page/ })).toBeTruthy();
    expect(fetchMock).toHaveBeenLastCalledWith("/api/miniapp/players?page=2&filter=all", expect.anything());
    expect((screen.getByRole("button", { name: "Next" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("says when nothing matches", async () => {
    stubFetch(() => paged([]));

    render(<MyPlayersList initData="x" />);

    expect(await screen.findByText("No players here yet.")).toBeTruthy();
  });
});

describe("PlayerDetail", () => {
  /**
   * Stubs the card and the files of the player.
   *
   * @param files - The files the API returns.
   * @returns The fetch stub.
   */
  function stubPlayer(files: unknown[]) {
    return stubFetch((url) =>
      url.includes("/files")
        ? paged(files)
        : {
            id: PLAYER_ID,
            name: "Arash Tehrani",
            category: "Men",
            nationalityCode: "IR",
            nationalityName: "Iran",
            positions: ["OH"],
            status: "Waiting",
            analystName: "Reza Ahmadi",
            hasPhoto: false,
            folderCount: 1,
            folders: [{ id: "f1", name: "SS-SPORT Arash Tehrani", access: "Lost" }],
            canEditDetails: true,
            canChangePhoto: true,
          },
    );
  }

  it("shows the card, the folders and the files in page order with the hero starred", async () => {
    stubPlayer([
      { id: "a1", kind: "Video", title: "Highlights 25-26", tags: ["Highlight"], sortOrder: 0, visibility: "Visible", state: "Ready", isHero: true },
      { id: "a2", kind: "Document", title: "Season stats", tags: ["Stats"], sortOrder: 1, visibility: "Hidden", state: "Failed", isHero: false },
    ]);

    render(<PlayerDetail playerId={PLAYER_ID} initData="x" />);

    expect(await screen.findByRole("heading", { level: 1, name: "Arash Tehrani" })).toBeTruthy();
    expect(screen.getByText(/SS-SPORT Arash Tehrani/).textContent).toMatch(/access lost/i);
    const items = await screen.findAllByRole("listitem", { name: /file/i });
    expect(items.map((item) => item.textContent)).toEqual([
      expect.stringMatching(/★ Highlights 25-26.*Video · Highlight · Ready/),
      expect.stringMatching(/Season stats.*Document · Stats · Failed · Hidden/),
    ]);
  });

  it("says when the player has no files yet", async () => {
    stubPlayer([]);

    render(<PlayerDetail playerId={PLAYER_ID} initData="x" />);

    expect(await screen.findByText(/No files yet/)).toBeTruthy();
  });

  it("opens the photo and details editor from the detail", async () => {
    stubPlayer([]);

    render(<PlayerDetail playerId={PLAYER_ID} initData="x" />);
    fireEvent.click(await screen.findByRole("button", { name: "Edit player" }));

    expect(await screen.findByRole("tab", { name: "Update photo & details" })).toBeTruthy();
  });
});
