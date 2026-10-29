import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { UpdatePlayer } from "./update-player";

const PLAYER_ID = "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e";

/**
 * Builds a player card as the API returns it.
 *
 * @param changes - Fields to change.
 * @returns The card.
 */
function card(changes: Record<string, unknown> = {}) {
  return {
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
    folders: [{ id: "f1", name: "SS-SPORT Arash Tehrani" }],
    canEditDetails: true,
    canChangePhoto: true,
    ...changes,
  };
}

/**
 * Stubs the API routes update mode calls.
 *
 * @param player - The card `GET /api/miniapp/players/{id}` returns.
 * @returns The fetch stub.
 */
function stubApi(player = card()) {
  const fetchMock = vi.fn((url: string, init?: RequestInit) => {
    if (url.startsWith("/api/miniapp/countries")) {
      return Promise.resolve(Response.json([{ code: "IR", name: "Iran" }, { code: "IT", name: "Italy" }]));
    }

    if (url.startsWith("/api/miniapp/drive-folder")) {
      return Promise.resolve(
        Response.json({ folderId: "newFolder_0001", name: "SS-SPORT Arash 25-26", videoCount: 4, documentCount: 0, scoutCount: 0, otherCount: 0, totalBytes: 2048, isTruncated: false }),
      );
    }

    if (url.endsWith("/folders")) {
      return Promise.resolve(Response.json({ id: "f2", name: "SS-SPORT Arash 25-26" }, { status: 201 }));
    }

    if (init?.method === "PATCH") {
      return Promise.resolve(Response.json({ ...player, ...JSON.parse(init.body as string) }));
    }

    return Promise.resolve(Response.json(player));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("UpdatePlayer", () => {
  it("shows the existing player with the three choices, Add this folder first", async () => {
    stubApi();

    render(<UpdatePlayer playerId={PLAYER_ID} initData="x" />);

    expect(await screen.findByRole("heading", { name: "Update player" })).toBeTruthy();
    expect(screen.getByText("Arash Tehrani")).toBeTruthy();
    expect(screen.getByText(/Reza Ahmadi/)).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Add this folder" }).getAttribute("aria-selected")).toBe("true");
    expect(screen.getByRole("tab", { name: "Update photo & details" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "Just open the existing page" })).toBeTruthy();
  });

  it("adds a checked folder to the player", async () => {
    const fetchMock = stubApi();
    render(<UpdatePlayer playerId={PLAYER_ID} initData="x" initialFolderUrl="https://drive.google.com/drive/folders/newFolder_0001" />);
    const add = (await screen.findByRole("button", { name: "Add folder" })) as HTMLButtonElement;

    fireEvent.blur(screen.getByLabelText("Drive folder link"));
    await waitFor(() => expect(add.disabled).toBe(false));
    fireEvent.click(add);

    expect(await screen.findByText(/folder added/i)).toBeTruthy();
    expect(fetchMock).toHaveBeenCalledWith(`/api/miniapp/players/${PLAYER_ID}/folders`, expect.objectContaining({ method: "POST" }));
  });

  it("saves only the changed details", async () => {
    const fetchMock = stubApi();
    render(<UpdatePlayer playerId={PLAYER_ID} initData="x" />);
    fireEvent.click(await screen.findByRole("tab", { name: "Update photo & details" }));

    fireEvent.change(await screen.findByLabelText("Nationality"), { target: { value: "ita" } });
    fireEvent.click(await screen.findByRole("option", { name: /Italy/ }));
    fireEvent.click(screen.getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText(/changes saved/i)).toBeTruthy();
    const patch = fetchMock.mock.calls.find(([, init]) => init?.method === "PATCH")!;
    expect(JSON.parse(patch[1]!.body as string)).toEqual({ nationalityCode: "IT" });
  });

  it("tells another analyst who can change the details, and hides the photo step", async () => {
    stubApi(card({ canEditDetails: false, canChangePhoto: false }));
    render(<UpdatePlayer playerId={PLAYER_ID} initData="x" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Update photo & details" }));

    expect(await screen.findByText(/Only Reza Ahmadi or an admin/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Save changes" })).toBeNull();
    expect(screen.queryByLabelText("Player photo")).toBeNull();
  });

  it("explains where the player page will be", async () => {
    stubApi();
    render(<UpdatePlayer playerId={PLAYER_ID} initData="x" />);

    fireEvent.click(await screen.findByRole("tab", { name: "Just open the existing page" }));

    expect(await screen.findByText(/viewer site/i)).toBeTruthy();
  });
});
