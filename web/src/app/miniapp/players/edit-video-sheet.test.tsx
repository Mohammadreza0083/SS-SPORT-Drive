import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { PlayerFile } from "@/lib/miniapp";
import { EditVideoSheet } from "./edit-video-sheet";
import { PlayerDetail } from "./player-detail";

const PLAYER_ID = "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e";

/**
 * Builds a file as the API returns it.
 *
 * @param changes - Fields to change.
 * @returns The file.
 */
function video(changes: Partial<PlayerFile> = {}): PlayerFile {
  return {
    id: "a1",
    kind: "Video",
    title: "Highlights 25-26",
    tags: ["Highlight"],
    sortOrder: 0,
    visibility: "Visible",
    state: "Ready",
    isHero: false,
    ...changes,
  };
}

/**
 * Stubs fetch: a PATCH answers with the file merged with the sent fields (or the given failure), anything else with `other`.
 *
 * @param other - Builds the body for non-PATCH requests.
 * @param failure - A ProblemDetails status and code to answer PATCHes with, if any.
 * @returns The fetch stub.
 */
function stubFetch(other: (url: string) => unknown = () => ({}), failure?: { status: number; code: string }) {
  const fetchMock = vi.fn((url: string, init?: RequestInit) => {
    if (init?.method === "PATCH") {
      if (failure) {
        return Promise.resolve(Response.json({ status: failure.status, code: failure.code }, { status: failure.status }));
      }

      const sent = JSON.parse(init.body as string) as Record<string, unknown>;
      return Promise.resolve(Response.json({ ...video(), ...sent, visibility: sent.hidden ? "Hidden" : "Visible" }));
    }

    return Promise.resolve(Response.json(other(url)));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/**
 * The body of the last PATCH the stub received.
 *
 * @param fetchMock - The fetch stub.
 * @returns The parsed JSON body.
 */
function lastPatch(fetchMock: ReturnType<typeof stubFetch>): unknown {
  const call = fetchMock.mock.calls.findLast(([, init]) => init?.method === "PATCH");
  return call ? JSON.parse(call[1]!.body as string) : undefined;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("EditVideoSheet", () => {
  it("sends only what changed: title, every tag and the hero choice", async () => {
    const fetchMock = stubFetch();
    const onSaved = vi.fn();

    render(<EditVideoSheet playerId={PLAYER_ID} file={video()} canHide={false} initData="x" onSaved={onSaved} onClose={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Best spikes" } });
    fireEvent.click(screen.getByRole("button", { name: "Spike" }));
    fireEvent.click(screen.getByLabelText("Hero video"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());
    expect(fetchMock).toHaveBeenCalledWith(`/api/miniapp/players/${PLAYER_ID}/files/a1`, expect.objectContaining({ method: "PATCH" }));
    expect(lastPatch(fetchMock)).toEqual({ title: "Best spikes", tags: ["Highlight", "Spike"], isHero: true });
  });

  it("shows Hide to admins only, and sends it on Save", async () => {
    const fetchMock = stubFetch();

    render(<EditVideoSheet playerId={PLAYER_ID} file={video()} canHide={false} initData="x" onSaved={vi.fn()} onClose={vi.fn()} />);
    expect(screen.queryByRole("button", { name: "Hide from viewers" })).toBeNull();
    cleanup();

    render(<EditVideoSheet playerId={PLAYER_ID} file={video()} canHide initData="x" onSaved={vi.fn()} onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Hide from viewers" }));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(lastPatch(fetchMock)).toEqual({ hidden: true }));
  });

  it("does not offer a hidden file or a document as the hero", () => {
    stubFetch();

    render(<EditVideoSheet playerId={PLAYER_ID} file={video({ kind: "Document" })} canHide={false} initData="x" onSaved={vi.fn()} onClose={vi.fn()} />);

    expect(screen.queryByLabelText("Hero video")).toBeNull();
  });

  it("explains a refused edit", async () => {
    stubFetch(undefined, { status: 422, code: "asset.hero_hidden" });

    render(<EditVideoSheet playerId={PLAYER_ID} file={video()} canHide={false} initData="x" onSaved={vi.fn()} onClose={vi.fn()} />);
    fireEvent.click(screen.getByLabelText("Hero video"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/hidden video cannot be the hero/i);
  });

  it("says when nothing changed, without calling the API", () => {
    const fetchMock = stubFetch();

    render(<EditVideoSheet playerId={PLAYER_ID} file={video()} canHide={false} initData="x" onSaved={vi.fn()} onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(screen.getByRole("status").textContent).toBe("Nothing has changed yet.");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("PlayerDetail with the edit-video sheet", () => {
  /**
   * Builds the card with the given file permissions.
   *
   * @param canEditFiles - Whether the caller may edit files.
   * @returns The card.
   */
  function card(canEditFiles: boolean) {
    return {
      id: PLAYER_ID,
      name: "Arash Tehrani",
      category: "Men",
      nationalityCode: "IR",
      nationalityName: "Iran",
      positions: ["OH"],
      status: "Published",
      analystName: "Reza Ahmadi",
      hasPhoto: false,
      folderCount: 0,
      folders: [],
      canEditDetails: false,
      canChangePhoto: canEditFiles,
      canEditFiles,
      canHideFiles: false,
    };
  }

  it("opens the sheet from a file and reloads the files after saving", async () => {
    let titles = ["Highlights 25-26"];
    const fetchMock = stubFetch((url) =>
      url.includes("/files") ? { items: titles.map((title) => video({ title })), page: 1, pageSize: 20, totalCount: 1, totalPages: 1 } : card(true),
    );

    render(<PlayerDetail playerId={PLAYER_ID} initData="x" />);
    fireEvent.click(await screen.findByRole("button", { name: "Edit Highlights 25-26" }));
    expect(screen.getByRole("dialog", { name: "Edit video" })).toBeTruthy();

    titles = ["Best spikes"];
    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Best spikes" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("button", { name: "Edit Best spikes" })).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes("/files?")).length).toBe(2);
  });

  it("offers no edit buttons to someone who may not edit the files", async () => {
    stubFetch((url) => (url.includes("/files") ? { items: [video()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 } : card(false)));

    render(<PlayerDetail playerId={PLAYER_ID} initData="x" />);

    expect(await screen.findByRole("listitem", { name: /file Highlights/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /^Edit Highlights/ })).toBeNull();
  });
});
